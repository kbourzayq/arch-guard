# Architecture Guardian for .NET

Define architecture boundaries in repository-owned policy and check C# dependencies
with source evidence and remediation guidance.

## Install an integration

| Package | Entry point | Requirements |
| --- | --- | --- |
| `ArchitectureGuardian.Cli` | `archguard` scans, inspection and CI gates | .NET 10 plus an SDK compatible with the target repository |
| `ArchitectureGuardian.Mcp` | `archguard-mcp` local stdio server | Same runtime/workspace requirements as CLI |
| `ArchitectureGuardian.Analyzers` | C# compiler diagnostics | .NET Standard 2.0 / Roslyn 4.8+ host; no standalone .NET 10 dependency |

For the CLI or MCP server:

```sh
dotnet tool install --global ArchitectureGuardian.Cli --version 0.2.0
dotnet tool install --global ArchitectureGuardian.Mcp --version 0.2.0
archguard --version
archguard --help
```

Keep the global tool directory on PATH. Pin matching versions when comparing
integrations. The [repository README](https://github.com/kbourzayq/arch-guard/blob/main/README.md)
contains complete global/local installation, updates, removal and first-use steps.
No source build is required to use published packages.

## CLI and MCP

From the target repository root, create a policy using the
[first-scan guide](https://github.com/kbourzayq/arch-guard/blob/main/docs/getting-started.md).
Authorize its build logic before restore and workspace evaluation. Replace
`MyApp.slnx` with your solution:

```sh
dotnet restore MyApp.slnx -p:Configuration=Release
archguard scan MyApp.slnx --policy architecture.guardian.yaml --configuration Release --trusted
```

Exit `0` means completed coverage and a passing gate; `1` means a completed failing
gate; `2` leaves compliance unresolved. Scans never restore automatically.

Configure an MCP stdio client with the absolute installed `archguard-mcp` path and
an explicit workspace root. The five read-only tools are `list_rules`,
`get_architecture`, `analyze_solution`, `explain_violation` and `analyze_changes`.
Workspace analysis requires operator trust. Follow
[MCP setup](https://github.com/kbourzayq/arch-guard/blob/main/docs/mcp.md) for client
configuration and response contracts. The optional
[agent skill](https://github.com/kbourzayq/arch-guard/blob/main/docs/agent-workflow.md)
is installed separately from the repository files.

## Compiler analyzer

Reference the analyzer in participating projects:

```xml
<PackageReference Include="ArchitectureGuardian.Analyzers" Version="0.2.0" PrivateAssets="all" />
<AdditionalFiles Include="$(MSBuildThisFileDirectory)architecture.guardian.compiler.xml" />
```

Place these items in an `ItemGroup` in a root props file, or adapt paths for your
project. Central package management declares the version in `Directory.Packages.props`.
Provide an XML ownership snapshot mapping every participating assembly and configure
severities through `.editorconfig`. The analyzer reads XML, not YAML; missing or
invalid configuration produces AG000. Installing the package alone does not
configure architecture checks.

The analyzer checks AG001, AG002 and AG004–AG006 in each compilation, without
whole-solution coverage or AG003 cycle checks. Retain an independent CLI gate.
See [complete analyzer setup](https://github.com/kbourzayq/arch-guard/blob/main/docs/compiler-analyzer.md).

## References

- [Rule catalog](https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md)
- [Compatibility and releases](https://github.com/kbourzayq/arch-guard/blob/main/docs/releases.md)
- [Release notes](https://github.com/kbourzayq/arch-guard/blob/main/CHANGELOG.md)
- [Troubleshooting](https://github.com/kbourzayq/arch-guard/blob/main/docs/troubleshooting.md)
- [Source development](https://github.com/kbourzayq/arch-guard/blob/main/docs/installation.md)
- [Support](https://github.com/kbourzayq/arch-guard/issues)
- [MIT license](https://github.com/kbourzayq/arch-guard/blob/main/LICENSE)
