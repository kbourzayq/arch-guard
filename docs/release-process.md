# Release engineering

## Build and verify packages

Run from the checkout root with Git, Python 3.10+, the pinned SDK and the Python
[Commitizen](https://commitizen-tools.github.io/commitizen/) tool on PATH. The
tooling version is pinned in `eng/requirements-release.txt`; an existing matching
installation can be reused. In an activated Python virtual environment, install it
with `python -m pip install -r eng/requirements-release.txt`.

```sh
python3 eng/packages.py pack
python3 eng/packages.py verify
python3 eng/packages.py smoke
```

Use an empty output directory for packing. If `artifacts/packages` already holds
a previous build, choose another directory with `--directory` and pass that same
directory to `pack`, `verify` and `smoke`.

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
bundled application/build-host files and the portable analyzer dependency layout.
Smoke tests use fresh tool installations and package/HTTP caches outside repository
build defaults, with a local-only tool feed. They run CLI version/help/explanation,
reject an unknown rule, load a three-project SDK workspace, require all six rules
in a completed failing scan, and check operational exit 2 for a missing input.
MCP initializes over stdio, verifies its version and five tools, inspects ownership,
and explains a current diagnostic. The client corrects fixture source and its
project reference without relaxing policy; independent CLI and MCP scans must then
pass. A real compiler consumer checks forbidden and allowed uses,
policy changes, AG005/AG006 and absence of analyzer-load errors.
It checks function-pointer aliases in YAML and legacy XML, including all five
compiler rules and unused-import silence.
The compiler consumer maps `ArchitectureGuardian.Analyzers` exclusively to the
local feed, checks that a missing local artifact fails restore, and verifies the
restored package's SHA-256 against the supplied artifact. SDK reference packages
can still come from nuget.org; a published analyzer at the same version cannot
substitute for the package under test.

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

See the [MVP release sign-off record](release-signoff-0.3.0.md) for candidate evidence,
the squash-history changelog boundary and the remaining maintainer gates.

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
push runs during PR validation. Review the proposed increment before creating a
release; ensure prior release tags are available in the local history.

If a reviewed release PR has already synchronized the version using
`--version-files-only`, do not run `cz bump` again for that candidate. After
maintainer approval, tag its exact approved merged commit at the prepared version;
the [0.3.0 sign-off record](release-signoff-0.3.0.md) gives this candidate's
finalization steps. The ordinary bump workflow above applies to releases whose
version has not already been prepared.

`CHANGELOG.md` is generated; do not maintain entries manually. Commitizen bump
creates the versioned heading required by release verification. Use `cz changelog`
only when intentionally regenerating notes, and review its output. See Commitizen's
[bump](https://commitizen-tools.github.io/commitizen/commands/bump/) and
[configuration](https://commitizen-tools.github.io/commitizen/config/bump/)
documentation for prereleases and version-file synchronization.

The repository-hosted agent skill is distributed separately from NuGet. Review
skill instructions and client examples when interfaces change; package version
alignment does not install or update a copied skill.

Before publishing, a maintainer must verify the release setup:

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

Generated notes and successful local builds do not establish public availability.
Verify the tagged workflow and each package feed after publication; the
[README](../README.md) documents the published installation path.
