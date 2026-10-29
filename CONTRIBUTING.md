# Contributing to Architecture Guardian

Thank you for helping improve Architecture Guardian. Start with the
[product overview](README.md), [documentation](docs/README.md) and
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44). For a substantial change,
open or discuss an issue so the intended behavior and scope are clear before implementation.

## Set up a development checkout

For consumer installation use the [README](README.md); for source publish and
local package commands see [local development](docs/installation.md).
Install the exact SDK selected by [global.json](global.json). SDK roll-forward is
disabled; apply SDK patch updates manually by changing its version. Then run:

```sh
git clone https://github.com/kbourzayq/arch-guard.git
cd arch-guard
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
```

The solution contains eight production projects and three test projects. Source
organization, dependency direction and the two analysis engines are described in
the [architecture overview](docs/architecture/overview.md).
[AGENT.md](AGENT.md) contains the repository's coding and validation conventions.

Use a focused semantic branch such as `fix/123-handle-missing-reference` or
`docs/improve-installation`. Preserve unrelated local changes. Do not commit
published outputs, test results, personal settings, credentials or local tool indexes.

## Make a change

- Keep rule evaluation in Core, workspace integration in Analysis and protocol or
  console behavior in its host. Keep Core independent of external packages and adapters.
- Preserve the difference between completed, incomplete, failed and cancelled work.
  Missing context must not become a passing analysis.
- Keep diagnostic identities and ordering deterministic. Test behavior, evidence
  and failure paths at the level affected by the change.
- Use the existing nullable, warning and formatting settings. Document public APIs
  with XML comments and keep namespaces aligned with responsibility folders.
- Update user guides when commands, configuration or behavior change. Update the
  authoritative source for derived artifacts rather than hand-editing generated output.
- When `.codegraph/` is present, follow the CodeGraph discovery instructions in
  [AGENT.md](AGENT.md). Index creation is optional and is not part of contribution setup.

## Run the required checks

From the repository root, before submitting code, project or dependency changes:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
dotnet format ArchitectureGuardian.slnx --verify-no-changes --no-restore
dotnet test ArchitectureGuardian.slnx --configuration Release --no-build --no-restore -m:1
```

Build warnings, including missing public API documentation, fail the build.
CI runs these checks on Linux, Windows and macOS. Passing locally does not replace
the required CI jobs. Dependency-boundary tests evaluate Debug and Release and
enforce solution membership and package/project reference direction.

All three test assemblies use xUnit v3 and build as .NET 10 executables. The
centrally versioned `xunit.v3.mtp-off` package disables Microsoft Testing Platform
integration. `dotnet test` uses VSTest through `xunit.runner.visualstudio` 4.0.1
and `Microsoft.NET.Test.Sdk` 18.11.0, including the category filters and TRX logger
shown here and in CI. Async fixtures implement xUnit v3's `ValueTask` lifetime
methods; keep resource cleanup in `DisposeAsync`, which xUnit calls in preference
to `IDisposable.Dispose` when both are implemented.
Use `TestContext.Current.CancellationToken` for cancellable test operations unless
the scenario deliberately supplies its own token. Cleanup that must restore shared
fixtures after cancellation uses an explicit `CancellationToken.None`. Workspace
test assemblies use `[assembly: Parallelization(MaxThreads = 2)]` to bound
concurrent collections.

For fast feedback after a build:

```sh
dotnet test tests/ArchitectureGuardian.Tests/ArchitectureGuardian.Tests.csproj --configuration Release --no-build --no-restore --filter "Category!=Integration"
```

Use `--filter "Category=Integration"` for the unit-test project's MSBuild/Roslyn
workspace integration tests. CLI process and evaluated dependency checks live in
`ArchitectureGuardian.ArchitectureTests`; run the full solution suite before submitting
code. On each OS, CI runs architecture/CLI checks and unit/compiler checks on
independent runners. Each test host retains its two-collection limit and `-m:1`;
this removes the wait between heavy suites without oversubscribing one runner.
The unit runner executes both unit-test assemblies sequentially. The existing
`Build and test (<os>)` check names are aggregate gates: both full test matrices
must succeed, including schema validation and all package-consumer checks, before
any of those gates can pass.
All tests still run on Linux, Windows and macOS. Fixture restore and individual
workspace CLI checks retain bounded process deadlines; trusted MCP scenarios use a
three-minute aggregate budget for their multiple full workspace calls. The process
helper disables build-worker reuse and applies its deadline through output draining.
Integration fixtures use isolated copies and prepare their required contexts
through the fixture preparation solutions. Deliberately broken inputs are excluded
from preparation; do not fix those intentional failures.
The reference fixtures restore their four configuration graphs serially in one
MSBuild process through `tests/Fixtures/ReferenceSolutions/Preparation.proj`.
Reference cases prepare one immutable graph, repeat rule evaluation against it,
and compare IDs and operational issues with a fresh CLI process. CLI scenarios
reuse their independently executed baseline update/scan for byte-repeatability
checks and combine inspection assertions for the same policy settings. They retain
cold-process parity, all mutation cases and incomplete-coverage checks.
The architecture suite starts its measured longest collections first to keep both
bounded workers busy; update `WorkspaceCollectionOrderer` if profiling changes those priorities.
CI caches NuGet's global packages using the committed lock files as its cache key;
locked restore still runs on every build.

For reporting changes, also validate serializer samples and real exports against
the JSON v1 and vendored OASIS SARIF 2.1.0 schemas (CI runs this on each OS):

```sh
python3 -m pip install jsonschema==4.26.0
python3 eng/test-reports.py
```

Use a virtual environment when your Python installation requires one. The validator
uses the build-only `tests/Fixtures/Reports` executable to generate 20 JSON/SARIF
samples in one process, covering completion, severities, baseline states, failure,
cancellation and located/unlocated evidence. It then restores isolated package-free
inputs once and performs four real CLI scans for stdout/file equivalence and repeatability.
The sample executable is built in the same configuration as the architecture tests;
run the full Release solution build above before the script. File failures and
baseline/coverage host behavior remain covered by the normal xUnit suite. No new
runtime packages are needed. Report schemas and mappings are documented in
[reports](docs/reports.md).

For CI gate changes, also run `python3 eng/test-quality-gate.py` after the Release
build. It runs the shared CI runner against real CLI scans, covering blocking
findings, accepted debt, operational failures and retained JSON/SARIF artifacts.
CI runs this check on all three operating systems. The consumer pipeline templates
and checkout/restore requirements are documented in [CI quality gates](docs/ci.md).

For performance work, run the .NET [BenchmarkDotNet project](benchmarks/README.md)
and include its generated Markdown report when updating published measurements.
The [performance guide](docs/performance.md) describes reproducible inputs, measured
budgets, memory scope and opt-in local telemetry. The benchmark project has its own
committed lock file and builds outside the shipping solution. Verify its formatting
with `dotnet format benchmarks/ArchitectureGuardian.Benchmarks.csproj --verify-no-changes --no-restore`.
The benchmark entry point handles input preparation and standard report generation;
no Python benchmarking scripts or custom JSON baseline are required. CI builds,
formats and discovers the benchmark suite on all three supported operating systems
without running performance measurements on shared runners.

To validate this checkout with its current analyzer and all six CLI rules after
locked restore, run `python3 eng/validate-repository.py`. See
[manual validation](docs/manual-validation.md) for compiler feedback, negative cases
and the AG003 solution-only boundary. White-box tests belong to the Guardian module
and retain their separate Tests layer.

For compiler package changes, also build and verify a consumer package:

```sh
dotnet pack src/ArchitectureGuardian.Analyzers --configuration Release --no-restore --output artifacts/analyzers
python3 eng/test-analyzer-package.py artifacts/analyzers/ArchitectureGuardian.Analyzers.0.3.0.nupkg --sdk 10.0.402
```

The package check requires Python 3 and the exact selected SDK installed locally.
It rejects public-feed fallback for the analyzer and verifies the restored package
against the supplied artifact by SHA-256.
CI separately checks compiler consumers with the same pinned SDK on each OS;
see [host compatibility](docs/compiler-analyzer.md#host-compatibility-and-validation).

For distribution or release changes, validate all three packages:

Use the installed Python Commitizen version pinned in `eng/requirements-release.txt`.
If needed, install that requirements file in an activated Python virtual environment
with `python -m pip install -r eng/requirements-release.txt`.

```sh
python3 eng/packages.py pack
python3 eng/packages.py verify
python3 eng/packages.py smoke
```

Use an empty output directory (or pass `--directory` for another directory).
These checks enforce package metadata, the source version and release notes,
dependency boundaries and reproducible payloads, then install the tools into an
isolated local-feed environment and run real CLI/MCP workspace and analyzer builds.
CI runs package consumers on all three operating systems. The
[release policy](docs/releases.md) describes the tagged workflow, checksum checks,
compatibility promises and maintainer configuration needed before publishing.

For documentation-only changes, verify commands, local links, headings and fenced
examples. Keep published-install examples separate from source-development steps.
The public Astro/Starlight website reuses these Markdown sources at build time.
See [website development](website/README.md) for the source-to-page map, Node setup,
`npm ci` / `npm run build`, browser checks and GitHub Pages settings. Edit the
authoritative document rather than the ignored generated website copy. Changes to
mapped Markdown also run website validation in the dedicated docs workflow.
When moving versioned install examples, update `.cz.toml` `version_files` so
Commitizen still synchronizes them. Validate the portable
`skills/architecture-guardian/SKILL.md` against the MCP/CLI contracts when changing
the agent workflow; test completion and gate handling separately. Runtime tests are unnecessary unless the documented behavior changes.
Keep machine-specific paths out of committed examples and label any replacement values.

## Change dependencies deliberately

NuGet uses the sources in [NuGet.Config](NuGet.Config). Lock files are committed.
Author participating package versions in the root `Directory.Packages.props`;
project `PackageReference` items retain dependency ownership and asset/privacy
metadata without inline versions. Conditional central entries keep the analyzer
test project's Roslyn 4.8 baseline separate from the solution-workspace packages.
Core has no package references, and central transitive pinning is disabled.
The analyzer test project explicitly references private PKCS 10.0.13 because
Analyzer.Testing 1.1.4 resolves vulnerable PKCS 9.0.6 through NuGet.Packaging 7.0.3
without that reference (GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx).

The Workspaces, ReferenceSolutions and AgentCorrection fixture roots each carry a
`Directory.Packages.props` opt-out, copied with their inputs. They restore
independently; the build-only generator retains its own explicit package version
and committed lock file. Keep intentional failed-restore inputs unchanged.
When intentionally updating packages:

```sh
dotnet restore ArchitectureGuardian.slnx --force-evaluate
```

Review the lock-file diff, explain why the dependency belongs in its component,
then rerun the required checks. Do not disable locked restore, architecture rules,
warnings or tests to make an unrelated failure disappear.

## Submit a pull request

Use a Conventional Commit subject, for example:

```text
fix(cli): preserve incomplete workspace coverage
docs: clarify source installation and MCP setup
```

Use the Python Commitizen tool to compose and validate commits:

```sh
cz commit
cz check --rev-range origin/main..HEAD
```

`.cz.toml` configures Conventional Commits, SemVer, synchronized .NET/package-example
versions and generated release notes. Use `cz bump --check-consistency --dry-run --yes`
to preview a release; actual release commits and tags follow the
[maintainer release process](docs/releases.md#prepare-a-release-with-commitizen).

In the pull request, describe the problem and resulting behavior, link the issue
when applicable, and record the checks actually run. Include relevant limitations,
unavailable checks and compatibility effects. For UI or output changes, a concise
before/after example helps review. Keep the description focused on the final change.

Respond to correctness, regression and documentation findings before requesting
another review. Keep unrelated cleanup in a separate change. Publishing, merging
and deployment follow the repository maintainer's review process.

## Release validation

Before publishing a release or accepting a coding-agent correction:

- Run the required locked restore, Release build, formatting and complete solution
  tests above. Require the normal GitHub Actions checks; an agent's passing MCP
  result does not replace CI or review.
- Restore the target solution in the intended configuration/framework outside MCP,
  then run a full CLI `scan` or MCP `analyze_solution`. Require completed coverage
  and a passed severity gate. Resolve operational issues; missing coverage is not
  compliance. Follow the [correction workflow](docs/mcp.md#coding-agent-correction-workflow)
  for findings and rerun after edits.
- Run the stdio correction integration test with the full suite. It verifies
  discovery, evidence-backed guidance, client-side correction and unchanged inputs
  across the read-only server calls.
- Require CLI `scan --base <release-base>` or MCP `analyze_changes` against an
  explicit release base, with completed coverage and a passed new-finding gate.
  Comparison covers staged, unstaged and non-ignored untracked working-tree inputs
  through full base/candidate contexts and each tree's policy. Missing refs,
  shallow history and failed snapshot loads block comparison approval. Prepare
  both snapshots' SDK/package requirements in advance; see [change analysis](docs/changes.md).
  Retain full-scan validation and normal CI: existing debt can pass a comparison.

## Report a bug

Open an [issue](https://github.com/kbourzayq/arch-guard/issues/new) with:

- The tool revision/version, operating system and relevant SDK versions.
- The exact command or MCP tool arguments, configuration and target framework.
- Expected behavior, actual output and exit code or MCP status.
- A small reproducer and relevant policy, with private source, paths and secrets removed.

Distinguish an architecture finding from an operational coverage issue. Both matter,
but an empty incomplete result is not evidence that a repository is compliant.
