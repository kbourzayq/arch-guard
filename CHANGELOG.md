# Release notes

## 0.1.0

Initial MVP distribution. Packages are prepared by the release workflow; this file
does not establish that a version has been published to nuget.org.

- `ArchitectureGuardian.Cli`: install the `archguard` .NET tool for policy-driven
  solution scans, inspection, explanations, baselines, full-context Git comparison
  and JSON v1 / SARIF 2.1.0 reports.
- `ArchitectureGuardian.Mcp`: install the `archguard-mcp` stdio tool with five
  read-only architecture and analysis tools. Operator workspace trust is explicit.
- `ArchitectureGuardian.Analyzers`: compiler feedback for AG001, AG002 and
  AG004–AG006, plus AG000 configuration errors. Requires an explicit XML policy;
  does not establish whole-solution coverage or detect module cycles.
- CLI/MCP require .NET 10 and an installed compatible SDK for workspace loading.
  The analyzer targets .NET Standard 2.0 with a Roslyn 4.8 API floor and has no
  standalone runtime or package dependencies.
- Package installation, analyzer consumer builds and CLI/MCP workspace smoke tests
  run on Linux, Windows and macOS before release publication.

See [compatibility and release policy](docs/releases.md) and
[known limitations](docs/troubleshooting.md#known-analysis-limitations).
