"""Exercise YAML policy, evaluated ownership and severity through the installed analyzer package."""

import json


def verify_yaml_consumer(root, version, framework, build, run):
    """Reuse the isolated XML consumer to prove migration without a CLI or authored assembly map."""
    (root / "Directory.Build.props").write_text(
        '<Project><ItemGroup>'
        f'<PackageReference Include="ArchitectureGuardian.Analyzers" Version="{version}" PrivateAssets="all"/>'
        '<AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.yaml"/>'
        '</ItemGroup></Project>')
    (root / "Orders/Orders.csproj").write_text(
        f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}</TargetFramework>'
        '<AssemblyName>Custom.Orders</AssemblyName></PropertyGroup>'
        '<ItemGroup><ProjectReference Include="../Billing/Billing.csproj"/></ItemGroup></Project>')
    (root / "Billing/Billing.csproj").write_text(
        f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}</TargetFramework>'
        '<AssemblyName>Custom.Billing</AssemblyName></PropertyGroup></Project>')
    # YAML alone now supplies severities. The XML file may remain on disk, but is no longer an AdditionalFile.
    (root / "Orders/.editorconfig").unlink()
    policy = root / "architecture.guardian.yaml"
    denied = '''version: 1
modules:
  - id: Orders
    projects: [Orders/*.csproj]
  - id: Billing
    projects: [Billing/*.csproj]
layers:
  - id: Application
    projects: [Orders/*.csproj]
  - id: Infrastructure
    projects: [Billing/*.csproj]
rules:
  AG001: error
  AG002: error
  AG003: error
  AG004: error
  AG005: error
  AG006: error
'''
    policy.write_text(denied)
    restore = ["dotnet", "restore", "Orders/Orders.csproj", "--packages", str(root / "packages"),
               "-p:NuGetAudit=false", "-m:1", "-nr:false"]
    assert run(restore, root).returncode == 0
    result = run(build, root)
    assert result.returncode != 0, "YAML forbids the dependency"
    for expected in ("error AG001", "error AG002", "error AG004"):
        assert expected in result.stdout, result.stdout
    for failure in ("AG000", "AD0001", "CS8032", "CS9057"):
        assert failure not in result.stdout + result.stderr, result.stdout

    permitted = denied.replace("    projects: [Orders/*.csproj]\n", "    projects: [Orders/*.csproj]\n    allowedDependencies: [module:Billing]\n", 1)
    permitted = permitted.replace("  - id: Application\n    projects: [Orders/*.csproj]",
        "  - id: Application\n    projects: [Orders/*.csproj]\n    allowedDependencies: [Infrastructure]")
    permitted = permitted.replace("  - id: Billing\n    projects: [Billing/*.csproj]",
        "  - id: Billing\n    projects: [Billing/*.csproj]\n    contracts:\n      - id: api\n        projects: [Billing/*.csproj]")
    policy.write_text(permitted)
    assert run(build, root).returncode == 0, "YAML permission/contract update did not restore passing build"

    # Standard per-file settings override the YAML severity, including suppression.
    (root / "Orders/.editorconfig").write_text(
        "root = true\n\n[*.cs]\ndotnet_diagnostic.AG001.severity = warning\n"
        "dotnet_diagnostic.AG002.severity = none\ndotnet_diagnostic.AG004.severity = none\n")
    policy.write_text(denied)
    overridden = run(build, root)
    assert overridden.returncode == 0 and "warning AG001" in overridden.stdout, overridden.stdout
    (root / "Orders/.editorconfig").unlink()
    policy.write_text(permitted.replace("  AG001: error\n", "").replace("  AG002: error\n", "").replace("  AG004: error\n", ""))
    assert run(build, root).returncode == 0, "Omitted compiler rules ran"

    # A third project appears only transitively in the source's compiler references.
    leaf = root / "Leaf"
    leaf.mkdir()
    (leaf / "Leaf.csproj").write_text(
        f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}</TargetFramework>'
        '<AssemblyName>Different.Leaf.Name</AssemblyName></PropertyGroup></Project>')
    (leaf / "Contract.cs").write_text("namespace Leaf { public interface IContract { } }")
    billing = root / "Billing/Billing.csproj"
    billing.write_text(billing.read_text().replace('</Project>',
        '<ItemGroup><ProjectReference Include="../Leaf/Leaf.csproj"/></ItemGroup></Project>'))
    (root / "Orders/Order.cs").write_text("public class Order : Billing.Service { public Leaf.IContract Value; }")
    transitive = permitted.replace("  - id: Billing\n", "  - id: Leaf\n    projects: [Leaf/*.csproj]\n  - id: Billing\n")
    transitive = transitive.replace("projects: [Billing/*.csproj]\nrules:", "projects: [Billing/*.csproj, Leaf/*.csproj]\nrules:")
    policy.write_text(transitive)
    assert run(restore, root).returncode == 0
    result = run(build, root)
    assert result.returncode != 0 and "error AG001" in result.stdout and "Different.Leaf.Name" in result.stdout, result.stdout
    assert "AG000" not in result.stdout + result.stderr, result.stdout

    # The design-time target must expose the same evaluated map, including after a YAML-only edit.
    design = run(["dotnet", "msbuild", "Orders/Orders.csproj", "-nologo", "-m:1", "-nr:false",
        "-t:Compile", "-p:DesignTimeBuild=true", "-p:BuildProjectReferences=false", "-p:SkipCompilerExecution=true",
        "-getItem:AdditionalFiles"], root)
    assert design.returncode == 0, design.stdout
    values = json.loads(design.stdout[design.stdout.index("{"):])["Items"]["AdditionalFiles"]
    assert any(item.get("ArchitectureGuardianAssemblyName") == "Different.Leaf.Name"
               or "Different.Leaf.Name" in item.get("ArchitectureGuardianAssemblyIdentity", "") for item in values), values

    # The same selectors survive configuration/TFM-specific assembly-name changes.
    allowed_leaf = transitive.replace("allowedDependencies: [module:Billing]", "allowedDependencies: [module:Billing, module:Leaf]")
    allowed_leaf = allowed_leaf.replace("  - id: Leaf\n    projects: [Leaf/*.csproj]",
        "  - id: Leaf\n    projects: [Leaf/*.csproj]\n    contracts:\n      - id: api\n        projects: [Leaf/*.csproj]")
    policy.write_text(allowed_leaf)
    for name in ("Orders", "Billing", "Leaf"):
        project = root / name / f"{name}.csproj"
        project.write_text(project.read_text().replace(f"<TargetFramework>{framework}</TargetFramework>",
            f"<TargetFrameworks>{framework};netstandard2.1</TargetFrameworks>").replace("</PropertyGroup>",
            f'<AssemblyName Condition="\'$(Configuration)\' == \'Release\' and \'$(TargetFramework)\' == \'netstandard2.1\'">Portable.{name}</AssemblyName></PropertyGroup>'))
    for configuration in ("Debug", "Release"):
        assert run(restore + [f"-p:Configuration={configuration}"], root).returncode == 0
        for target in (framework, "netstandard2.1"):
            context = build + [f"-p:Configuration={configuration}", f"-p:TargetFramework={target}"]
            assert run(context, root).returncode == 0, "Evaluated framework/configuration ownership failed"
    assert run(["dotnet", "clean", "Orders/Orders.csproj", "-m:1", "-nr:false"], root).returncode == 0
    cold = run(["dotnet", "msbuild", "Orders/Orders.csproj", "-nologo", "-m:1", "-nr:false",
        "-t:Compile", f"-p:TargetFramework={framework}", "-p:DesignTimeBuild=true",
        "-p:BuildProjectReferences=false", "-p:SkipCompilerExecution=true", "-getItem:AdditionalFiles"], root)
    assert cold.returncode == 0, cold.stdout
    cold_values = json.loads(cold.stdout[cold.stdout.index("{"):])["Items"]["AdditionalFiles"]
    assert any(item.get("ArchitectureGuardianAssemblyName") == "Different.Leaf.Name" for item in cold_values), cold_values
    policy.write_text("version: 2\n")
    invalid = run(build + [f"-p:TargetFramework={framework}"], root)
    assert invalid.returncode != 0 and "AG000" in invalid.stdout and "architecture.guardian.yaml(" in invalid.stdout, invalid.stdout
    print("YAML package passed defaults, overrides, selection, custom/transitive ownership and design-time metadata")
