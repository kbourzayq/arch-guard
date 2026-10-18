"""Build a real NuGet consumer with the requested SDK, outside repository build defaults."""

import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile


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
        assert dlls == ["analyzers/dotnet/cs/ArchitectureGuardian.Analyzers.dll"], dlls
        metadata = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith(".nuspec"))))
        version = next(node.text for node in metadata.iter() if node.tag.endswith("}version"))
        assert not any(node.tag.endswith("}dependency") for node in metadata.iter())

    with tempfile.TemporaryDirectory(prefix="archguard-compiler-") as directory:
        root = Path(directory)
        (root / "global.json").write_text(json.dumps({"sdk": {"version": args.sdk, "rollForward": "disable"}}))
        (root / "NuGet.Config").write_text(
            '<configuration><packageSources><clear/><add key="local" value="'
            + escape(str(package.parent), {'"': '&quot;'})
            + '"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
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
        (orders / ".editorconfig").write_text("root = true\n\n[*.cs]\ndotnet_diagnostic.AG001.severity = error\ndotnet_diagnostic.AG002.severity = error\n")
        policy = '''<ArchitectureGuardian version="1">
          <Module id="Orders"/><Module id="Billing"/>
          <Layer id="Application"/><Layer id="Infrastructure"/>
          <Assembly name="Orders.Application" module="Orders" layer="Application"/>
          <Assembly name="Billing.Application" module="Billing" layer="Infrastructure"/>
        </ArchitectureGuardian>'''
        snapshot = orders / "architecture.guardian.compiler.xml"
        snapshot.write_text(policy)
        # Isolate the local analyzer package so repeated runs cannot use a cached older build.
        assert run(["dotnet", "restore", "Orders/Orders.csproj", "--packages", str(root / "packages")], root).returncode == 0
        build = ["dotnet", "build", "Orders/Orders.csproj", "--no-restore", "-p:UseSharedCompilation=false"]
        forbidden = run(build, root)
        assert forbidden.returncode != 0, "Forbidden semantic dependency passed"
        for diagnostic in ("error AG001", "error AG002", "Order.cs("):
            assert diagnostic in forbidden.stdout, forbidden.stdout
        for failure in ("CS8032", "CS9057", "AD0001", "AG000"):
            assert failure not in forbidden.stdout + forbidden.stderr, forbidden.stdout
        snapshot.write_text(policy.replace('<Module id="Orders"/>', '<Module id="Orders" allows="module:Billing"/>')
                            .replace('<Layer id="Application"/>', '<Layer id="Application" allows="Infrastructure"/>'))
        assert run(build, root).returncode == 0, "Allowed dependency failed after AdditionalFile update"
        print(f"Analyzer package passed forbidden/allowed builds with SDK {args.sdk}")


if __name__ == "__main__":
    main()
