# Packages, compatibility and releases

Architecture Guardian publishes three independently installable packages at one
shared version. Install the CLI, MCP server or compiler analyzer according to the
workflow you need. Internal application components are not separately supported
NuGet library APIs.

| Package | Entry point / host | Dependency boundary |
| --- | --- | --- |
| `ArchitectureGuardian.Cli` | `archguard`, .NET 10 | Tool package includes its runtime dependencies and Roslyn build hosts; workspace loading uses an installed SDK |
| `ArchitectureGuardian.Mcp` | `archguard-mcp`, .NET 10 stdio | Same solution engine and SDK requirements as CLI; stdout is reserved for MCP |
| `ArchitectureGuardian.Analyzers` | C# compiler, .NET Standard 2.0 / Roslyn 4.8+ | Analyzer, portable Policy reader and YamlDotNet under `analyzers/dotnet/cs`, plus buildTransitive targets; no standalone runtime, Workspaces or separate NuGet dependencies |

See [installation](install.md), [compiler-host validation](compiler-analyzer.md#host-compatibility-and-validation)
and [workspace SDK selection](workspace-loading.md). Your repository must install the SDK required by
its inputs; a runtime alone cannot evaluate MSBuild projects. Linux, Windows and
macOS receive package smoke tests. Interactive IDE UI testing remains outside this
automated matrix; Roslyn 4.8 is an API floor, not a promise of vendor support for
obsolete hosts. Pin the same package version when comparing engines.

## Version and compatibility policy

- Versions use SemVer (`major.minor.patch`, optionally with a prerelease suffix).
  Release tags are exactly `v<Version>`; every package, CLI version, MCP initialize
  response and release-notes link derives from that version. Build metadata may
  append the source commit to the CLI/assembly informational version.
- Before 1.0, minor releases may change supported behavior or compatibility floors;
  patch releases fix defects while retaining documented contracts. From 1.0,
  incompatible public command, configuration or protocol changes require a major
  version. Review host-floor changes and migration guidance in release notes.
- Rule IDs retain their canonical meanings. New rules or changed defaults can
  affect findings; review these in release notes. JSON reports, YAML policy,
  compiler snapshots and baseline fingerprints have their own schema versions.
  Package SemVer does not silently redefine those schemas. CLI and MCP share the
  solution engine; compiler counts/identities remain intentionally different.
- NuGet versions and release tags are immutable. Select a new published version
  to receive a fix and keep the three integrations aligned when comparing behavior.

## Upgrade deliberately

Pin the same published version across integrations. Read [release notes](../CHANGELOG.md),
update tools/package references, reconnect MCP clients and rerun full scans in the
contexts you gate. Revisit a baseline only after reviewing any changed findings.
See [update and removal](install.md#update-or-remove).

YAML v1, JSON v1, SARIF 2.1.0 and baseline fingerprints are distinct contracts.
A package upgrade does not make a report from an incomplete scan usable or remove
the need to select configuration/framework. See [engine coverage](semantic-accuracy.md).
