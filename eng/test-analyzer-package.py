"""Build a real NuGet consumer with the requested SDK, outside repository build defaults."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from analyzer_yaml_consumer import verify_yaml_consumer


def run(arguments, cwd):
    result = subprocess.run(arguments, cwd=cwd, capture_output=True, text=True, timeout=180)
    print(result.stdout + result.stderr, flush=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--sdk", required=True)
    args = parser.parse_args()
    package = args.package.resolve()
    with zipfile.ZipFile(package) as archive:
        dlls = [name for name in archive.namelist() if name.endswith(".dll")]
        assert set(dlls) == {"analyzers/dotnet/cs/ArchitectureGuardian.Analyzers.dll",
            "analyzers/dotnet/cs/ArchitectureGuardian.Policy.dll", "analyzers/dotnet/cs/YamlDotNet.dll"}, dlls
        assert "buildTransitive/ArchitectureGuardian.Analyzers.targets" in archive.namelist()
        metadata = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith(".nuspec"))))
        version = next(node.text for node in metadata.iter() if node.tag.endswith("}version"))
        assert not any(node.tag.endswith("}dependency") for node in metadata.iter())

    with tempfile.TemporaryDirectory(prefix="archguard-compiler-") as directory:
        root = Path(directory)
        (root / "global.json").write_text(json.dumps({"sdk": {"version": args.sdk, "rollForward": "disable"}}))
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        local = ET.SubElement(sources, "add", key="local", value=str(package.parent))
        ET.SubElement(sources, "add", key="nuget", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(config, "packageSourceMapping")
        ET.SubElement(mapping, "clear")
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="local"),
                      "package", pattern="ArchitectureGuardian.Analyzers")
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget"), "package", pattern="*")
        framework = "net" + args.sdk.split(".")[0] + ".0"
        billing = root / "Billing"
        orders = root / "Orders"
        billing.mkdir()
        orders.mkdir()
        (billing / "Billing.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}</TargetFramework>'
            '<AssemblyName>Billing.Application</AssemblyName></PropertyGroup></Project>')
        (billing / "Service.cs").write_text("namespace Billing { public class Service { } }")
        (orders / "Orders.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}</TargetFramework>'
            '<AssemblyName>Orders.Application</AssemblyName>'
            '</PropertyGroup><ItemGroup><ProjectReference Include="../Billing/Billing.csproj"/>'
            f'<PackageReference Include="ArchitectureGuardian.Analyzers" Version="{version}" PrivateAssets="all"/>'
            '<AdditionalFiles Include="architecture.guardian.compiler.xml"/></ItemGroup></Project>')
        (orders / "Order.cs").write_text("public class Order : Billing.Service { }")
        (orders / ".editorconfig").write_text("root = true\n\n[*.cs]\ndotnet_diagnostic.AG001.severity = error\ndotnet_diagnostic.AG002.severity = error\ndotnet_diagnostic.AG004.severity = error\ndotnet_diagnostic.AG005.severity = error\ndotnet_diagnostic.AG006.severity = error\n")
        policy = '''<ArchitectureGuardian version="1">
          <Module id="Orders"/><Module id="Billing"/>
          <Layer id="Application"/><Layer id="Infrastructure"/>
          <Assembly name="Orders.Application" module="Orders" layer="Application"/>
          <Assembly name="Billing.Application" module="Billing" layer="Infrastructure"/>
        </ArchitectureGuardian>'''
        snapshot = orders / "architecture.guardian.compiler.xml"
        snapshot.write_text(policy)
        restore = ["dotnet", "restore", "Orders/Orders.csproj", "--packages", str(root / "packages"),
                   "-p:NuGetAudit=false", "-m:1", "-nr:false"]
        # A public package at the same version must never stand in for a missing local artifact.
        empty_feed = root / "empty-feed"
        empty_feed.mkdir()
        local.set("value", str(empty_feed))
        ET.ElementTree(config).write(root / "NuGet.Config", encoding="utf-8")
        print("Checking local analyzer isolation (expected restore failure)", flush=True)
        missing = run(restore, root)
        assert missing.returncode != 0 and "NU1101" in missing.stdout + missing.stderr, \
            "Missing local analyzer must fail restore instead of using nuget.org"
        assert "PackageSourceMapping" in missing.stdout + missing.stderr
        local.set("value", str(package.parent))
        ET.ElementTree(config).write(root / "NuGet.Config", encoding="utf-8")
        assert run(restore, root).returncode == 0
        assets = json.loads((orders / "obj/project.assets.json").read_text(encoding="utf-8"))
        library = next(value for name, value in assets["libraries"].items()
                       if name.split("/")[0].casefold() == "architectureguardian.analyzers")
        restored = list((root / "packages" / library["path"]).glob("*.nupkg"))
        assert len(restored) == 1 and hashlib.sha256(restored[0].read_bytes()).digest() == hashlib.sha256(package.read_bytes()).digest(), \
            "Restored analyzer differs from the package under test"
        print("Restored analyzer SHA-256 matches the local artifact", flush=True)
        build = ["dotnet", "build", "Orders/Orders.csproj", "--no-restore", "-p:UseSharedCompilation=false", "-m:1", "-nr:false"]
        forbidden = run(build, root)
        assert forbidden.returncode != 0, "Forbidden semantic dependency passed"
        for diagnostic in ("error AG001", "error AG002", "error AG004", "Order.cs("):
            assert diagnostic in forbidden.stdout, forbidden.stdout
        for failure in ("CS8032", "CS9057", "AD0001", "AG000"):
            assert failure not in forbidden.stdout + forbidden.stderr, forbidden.stdout
        permitted = policy.replace('<Module id="Orders"/>', '<Module id="Orders" allows="module:Billing"/>') \
            .replace('<Layer id="Application"/>', '<Layer id="Application" allows="Infrastructure"/>')
        snapshot.write_text(permitted)
        implementation = run(build, root)
        assert implementation.returncode != 0 and "error AG004" in implementation.stdout, implementation.stdout
        assert "error AG001" not in implementation.stdout and "error AG002" not in implementation.stdout, implementation.stdout
        snapshot.write_text(permitted.replace('layer="Infrastructure"/>', 'layer="Infrastructure" contract="api"/>'))
        assert run(build, root).returncode == 0, "Allowed contract failed after AdditionalFile update"
        contract = snapshot.read_text()
        snapshot.write_text(contract.replace('<Module id="Orders" allows="module:Billing"/>',
            '<Module id="Orders" allows="module:Billing" forbiddenNamespaces="Billing"/>'))
        restricted = run(build, root)
        assert restricted.returncode != 0 and "error AG005" in restricted.stdout and "Order.cs(" in restricted.stdout, restricted.stdout
        assert "modules.Orders.forbiddenNamespaces restriction 'Billing'" in restricted.stdout, restricted.stdout
        for failure in ("CS8032", "CS9057", "AD0001", "AG000", "error AG001", "error AG002", "error AG004"):
            assert failure not in restricted.stdout + restricted.stderr, restricted.stdout
        snapshot.write_text(contract)
        assert run(build, root).returncode == 0, "Removing AG005 restriction did not restore passing build"
        snapshot.write_text(contract.replace('<Layer id="Application" allows="Infrastructure"/>',
            '<Layer id="Application" allows="Infrastructure" infrastructureNamespaces="Billing"/>'))
        domain = run(build, root)
        assert domain.returncode != 0 and "error AG006" in domain.stdout and "Order.cs(" in domain.stdout, domain.stdout
        assert "layers.Application.domainInfrastructure.forbiddenNamespaces restriction 'Billing'" in domain.stdout, domain.stdout
        for failure in ("CS8032", "CS9057", "AD0001", "AG000", "error AG001", "error AG002", "error AG004", "error AG005"):
            assert failure not in domain.stdout + domain.stderr, domain.stdout
        snapshot.write_text(snapshot.read_text().replace('infrastructureNamespaces="Billing"',
            'infrastructureNamespaces="Billing" allowedInfrastructureNamespaces="Billing"'))
        assert run(build, root).returncode == 0, "AG006 namespace exception did not restore passing build"
        verify_yaml_consumer(root, version, framework, build, run)
        print(f"Analyzer package passed forbidden/allowed and AG005/AG006 builds with SDK {args.sdk}")


if __name__ == "__main__":
    main()
