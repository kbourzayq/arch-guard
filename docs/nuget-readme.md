# Architecture Guardian for .NET

Keep architectural boundaries enforceable as your C# codebase grows. Define modules,
layers and public contracts, then find forbidden dependencies with source evidence
and remediation guidance.

## Choose a package

| Package | Integration | Requirements |
| --- | --- | --- |
| `ArchitectureGuardian.Cli` | `archguard` command for solution scans and CI gates | .NET 10 plus an installed SDK compatible with the target repository |
| `ArchitectureGuardian.Mcp` | `archguard-mcp` read-only stdio server for coding agents | Same runtime and workspace SDK requirements as CLI |
| `ArchitectureGuardian.Analyzers` | C# compiler diagnostics from an explicit XML policy | .NET Standard 2.0 / Roslyn 4.8+ compiler host; no standalone .NET 10 dependency |

Install a published tool version using `dotnet tool install --global
ArchitectureGuardian.Cli` or `dotnet tool install --global ArchitectureGuardian.Mcp`.
Pin the same package version for CLI, MCP and analyzers when comparing results.
See [installation and local package feeds](https://github.com/kbourzayq/arch-guard/blob/main/docs/installation.md)
for complete commands, SDK requirements, updates and removal.

## CLI and MCP

Run `archguard --help` or `archguard explain AG001` to check installation.
Restore target projects before scanning and authorize their MSBuild logic explicitly.
CLI exit codes distinguish a passing completed scan (`0`), a failing completed
gate (`1`) and incomplete or failed analysis (`2`). Missing coverage never passes.

The MCP server registers five read-only tools: `get_architecture`, `list_rules`,
`analyze_solution`, `explain_violation` and `analyze_changes`. Set its absolute
executable path, workspace root and explicit operator trust in your stdio client.
Follow [MCP setup](https://github.com/kbourzayq/arch-guard/blob/main/docs/mcp.md)
or the [first-scan guide](https://github.com/kbourzayq/arch-guard/blob/main/docs/getting-started.md).

## Compiler analyzer

Reference `ArchitectureGuardian.Analyzers` with `PrivateAssets="all"` and provide
`architecture.guardian.compiler.xml` through `AdditionalFiles`. Every participating
assembly needs an ownership mapping. Missing or invalid configuration produces
AG000; installing the package alone does not configure architecture checks.

The analyzer checks AG001, AG002 and AG004–AG006 using the host compilation. It
neither opens a workspace nor establishes whole-solution coverage or module-cycle
compliance. Keep full CLI/MCP scans as your solution gate. Follow the complete
[analyzer installation, policy and compatibility guide](https://github.com/kbourzayq/arch-guard/blob/main/docs/compiler-analyzer.md).

## Documentation and support

- [Rule catalog and examples](https://github.com/kbourzayq/arch-guard/blob/main/docs/rule-catalog.md)
- [Version, compatibility and release policy](https://github.com/kbourzayq/arch-guard/blob/main/docs/releases.md)
- [Release notes](https://github.com/kbourzayq/arch-guard/blob/main/CHANGELOG.md)
- [Troubleshooting and known limitations](https://github.com/kbourzayq/arch-guard/blob/main/docs/troubleshooting.md)
- [Report an issue](https://github.com/kbourzayq/arch-guard/issues)
- [MIT license](https://github.com/kbourzayq/arch-guard/blob/main/LICENSE)
