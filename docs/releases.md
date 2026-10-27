# Packages, compatibility and releases

Architecture Guardian publishes three independently installable packages at one
shared version, managed by Python Commitizen in root `.cz.toml`. Its built-in
`version_files` setting synchronizes `Directory.Build.props` and versioned install
examples. Internal Core, Analysis,
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
  Commitizen uses `semver2`, Conventional Commits and `major_version_zero = true`
  for the current pre-1.0 compatibility policy.
  Release tags are exactly `v<Version>`; every package, CLI version, MCP initialize
  response and release-notes link derives from that version. Build metadata may
  append the source commit to the CLI/assembly informational version.
- Before 1.0, minor releases may change supported behavior or compatibility floors;
  patch releases fix defects while retaining documented contracts. From 1.0,
  incompatible public command, configuration or protocol changes require a major
  version; disable Commitizen's `major_version_zero` setting when adopting 1.0.
  Record host-floor changes and migration guidance in release notes.
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

Run from a clean checkout with Git, Python 3.10+, the pinned SDK and the Python
[Commitizen](https://commitizen-tools.github.io/commitizen/) tool on PATH. The
tooling version is pinned in `eng/requirements-release.txt`; an existing matching
installation can be reused. In an activated Python virtual environment, install it
with `python -m pip install -r eng/requirements-release.txt`.

```sh
python3 eng/packages.py pack
python3 eng/packages.py verify
python3 eng/packages.py smoke
```

The package scripts read `cz version --project` and verify that the .NET build
version agrees. They validate artifacts and consumer execution; version increments,
release commits, tags and changelog generation belong to Commitizen.

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

## Prepare a release with Commitizen

Use `cz commit` to create Conventional Commits and `cz check --rev-range
origin/main..HEAD` to validate a development branch. CI runs Commitizen checks on
the incoming PR commits (or the latest main/tag commit). Full history is available
to that check.

Preview the next release without changing files or creating tags:

```sh
cz bump --check-consistency --dry-run --yes
```

After maintainer release approval, use the standard Commitizen workflow:

```sh
cz bump --check-consistency --yes
```

Commitizen selects the increment from Conventional Commits, updates its version and
the configured .NET/example files, regenerates `CHANGELOG.md`, creates the release
commit and tags it as `v<Version>`. Review the resulting changes and run the required
release checks, then publish that commit and tag through the maintainer's process.
The tag starts the verified NuGet workflow. No automatic bump, tag creation or tag
push runs during PR validation. The dry run may propose a new minor version before
the first release because the existing feature history has no release tag yet.

`CHANGELOG.md` is generated; do not maintain entries manually. Use `cz changelog`
to regenerate development notes. Before the first release, history is under
`Unreleased`; Commitizen bump creates the versioned heading required by release
verification. This also lets Commitizen initialize its first release without an
invented previous tag. See Commitizen's [bump](https://commitizen-tools.github.io/commitizen/commands/bump/)
and [configuration](https://commitizen-tools.github.io/commitizen/config/bump/)
documentation for prereleases and version-file synchronization.

Before the first release, a maintainer must:

1. Own/reserve all three package IDs on nuget.org and set the GitHub repository
   variable `NUGET_USER` to the publishing account's profile name.
2. Create the GitHub `nuget` environment and configure its reviewers and permitted
   release tags to match the project's release process.
3. Configure [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for owner `kbourzayq`, repository `arch-guard`, workflow file `release.yml` and
   environment `nuget`, scoped to these package IDs. Only the publish job requests
   an OIDC token; no long-lived API key is stored in the repository.
4. Use the Commitizen workflow above to update versions, the generated
   [CHANGELOG.md](../CHANGELOG.md) and affected examples. Update analyzer release
   tracking when diagnostics change. Run the required contribution/release checks, review
   the change and tag that exact revision after approval through the maintainer's
   process. Tagging or publishing packages is separate from opening an implementation PR.

The generated notes describe a prepared version; neither those notes nor a
successful package build claims publication to nuget.org.
