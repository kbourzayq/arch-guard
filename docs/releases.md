# Packages, compatibility and releases

Architecture Guardian publishes three independently installable packages at one
shared version, authored in root `Directory.Build.props`. Internal Core, Analysis,
Configuration and Reporting projects are bundled application components, with no
separately supported NuGet library API. They are not packable.

| Package | Entry point / host | Dependency boundary |
| --- | --- | --- |
| `ArchitectureGuardian.Cli` | `archguard`, .NET 10 | Tool package includes its runtime dependencies and Roslyn build hosts; workspace loading uses an installed SDK |
| `ArchitectureGuardian.Mcp` | `archguard-mcp`, .NET 10 stdio | Same solution engine and SDK requirements as CLI; stdout is reserved for MCP |
| `ArchitectureGuardian.Analyzers` | C# compiler, .NET Standard 2.0 / Roslyn 4.8+ | Only its analyzer DLL under `analyzers/dotnet/cs`; no standalone runtime, Workspaces or NuGet dependencies |

See [installation](installation.md), [compiler-host validation](compiler-analyzer.md#host-compatibility-and-validation)
and [workspace SDK selection](workspace-loading.md). Local builds and CI use the
exact SDK in `global.json`. Consumer repositories must install the SDK required by
their inputs; a runtime alone cannot evaluate MSBuild projects. Linux, Windows and
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
  version. Record host-floor changes and migration guidance in release notes.
- Rule IDs retain their canonical meanings. New rules or changed defaults can
  affect findings; describe these in release notes. JSON reports, YAML policy,
  compiler snapshots and baseline fingerprints have their own schema versions.
  Package SemVer does not silently redefine those schemas. CLI and MCP share the
  solution engine; compiler counts/identities remain intentionally different.
- NuGet versions and release tags are immutable. Do not replace a published version
  with different bytes. Fix a failed release through a new version when necessary.
  Publication of three packages is not transactional; a partial feed failure needs
  maintainer investigation before retrying.

## Build and verify packages

Run from a clean checkout with Git, Python 3 and the pinned SDK:

```sh
python3 eng/packages.py pack
python3 eng/packages.py verify
python3 eng/packages.py smoke
```

The pack command restores committed lock files, builds Release packages with the
source commit recorded, then recompiles and packs again. It compares all package
payload bytes, including assemblies, metadata and readmes. NuGet-generated ZIP
timestamps, relationship metadata and core-properties identities are excluded; this
checks reproducible payloads, not byte-for-byte identical ZIP envelopes. Source,
SDK and locked dependencies must match. The original artifacts receive a SHA-256
manifest and are the ones installed, tested and published.

Verification requires exactly the three expected packages at the source version,
MIT license metadata, repository/commit, release notes, readmes with usable absolute
documentation links, tool entry points,
bundled application/build-host files and the dependency-free analyzer layout.
Smoke tests use fresh tool installations and package/HTTP caches outside repository
build defaults, with a local-only tool feed. They run CLI version/help/explanation,
reject an unknown rule, load a two-project SDK workspace, require a completed
failing scan, grant the dependency and require a passing scan. MCP initializes over
stdio, verifies its version and five tools, and performs the same failing/passing
solution analyses. A real compiler consumer checks forbidden and allowed uses,
policy changes, AG005/AG006 and absence of analyzer-load errors.

The compiler driver suite also tests the Roslyn 4.8 baseline. Full solution tests,
formatting, reporting-schema validation and independent architecture gate checks
remain required alongside package validation. No new runtime dependency is added.

## Release workflow

`.github/workflows/release.yml` runs on pushed `v*` tags. It invokes the reusable
CI workflow on the tagged commit. All three OS test matrices, schema/gate checks,
package content/reproducibility checks and clean package consumers must succeed
before the publish job can run. The publish job downloads the original tested
artifact, checks the tag/version/commit and SHA-256 manifest, then publishes those
packages to nuget.org without rebuilding them. Normal PR CI also runs package
validation. A local passing run does not establish that GitHub CI passed.

Before the first release, a maintainer must:

1. Own/reserve all three package IDs on nuget.org and set the GitHub repository
   variable `NUGET_USER` to the publishing account's profile name.
2. Create the GitHub `nuget` environment and configure its reviewers and permitted
   release tags to match the project's release process.
3. Configure [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for owner `kbourzayq`, repository `arch-guard`, workflow file `release.yml` and
   environment `nuget`, scoped to these package IDs. Only the publish job requests
   an OIDC token; no long-lived API key is stored in the repository.
4. Update `Version`, [CHANGELOG.md](../CHANGELOG.md), affected examples and analyzer
   release tracking together. Run the required contribution/release checks, review
   the change and tag that exact revision after approval through the maintainer's
   process. Tagging or publishing packages is separate from opening an implementation PR.

The initial `0.1.0` release notes describe the prepared MVP; neither this document
nor a successful package build claims publication to nuget.org.
