# Contributing to Architecture Guardian

Thank you for helping improve Architecture Guardian. Start with the
[product overview](README.md), [documentation](docs/README.md) and
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44). For a substantial change,
open or discuss an issue so the intended behavior and scope are clear before implementation.

## Set up a development checkout

Install the SDK selected by [global.json](global.json), then run:

```sh
git clone https://github.com/kbourzayq/arch-guard.git
cd arch-guard
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
```

The solution contains six production projects and three test projects. Source
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

For fast feedback after a build:

```sh
dotnet test tests/ArchitectureGuardian.Tests/ArchitectureGuardian.Tests.csproj --configuration Release --no-build --no-restore --filter "Category!=Integration"
```

Use `--filter "Category=Integration"` for the unit-test project's MSBuild/Roslyn
workspace integration tests. CLI process and evaluated dependency checks live in
`ArchitectureGuardian.ArchitectureTests`; run the full solution suite before submitting
code. CI runs test projects sequentially (`-m:1`), while the workspace-bearing test
assemblies cap concurrent collections at two. This bounds simultaneous MSBuild and
Roslyn work on shared runners without omitting tests. Fixture restore and individual
workspace CLI checks retain bounded process deadlines; trusted MCP scenarios use a
three-minute aggregate budget for their multiple full workspace calls. The process
helper disables build-worker reuse and applies its deadline through output draining.
Integration fixtures use isolated copies and prepare their required contexts
through the fixture preparation solutions. Deliberately broken inputs are excluded
from preparation; do not fix those intentional failures.

For compiler package changes, also build and verify a consumer package:

```sh
dotnet pack src/ArchitectureGuardian.Analyzers --configuration Release --no-restore --output artifacts/analyzers
python3 eng/test-analyzer-package.py artifacts/analyzers/ArchitectureGuardian.Analyzers.0.1.0.nupkg --sdk 10.0.301
```

The package check requires Python 3 and the exact selected SDK installed locally.
CI separately checks compiler consumers across its configured SDK/OS matrix;
see [host compatibility](docs/compiler-analyzer.md#host-compatibility-and-validation).

For documentation-only changes, verify commands, local links, headings and fenced
examples. Runtime tests are unnecessary unless the documented behavior changes.
Keep machine-specific paths out of committed examples and label any replacement values.

## Change dependencies deliberately

NuGet uses the sources in [NuGet.Config](NuGet.Config). Lock files are committed.
Author participating package versions in the root `Directory.Packages.props`;
project `PackageReference` items retain dependency ownership and asset/privacy
metadata without inline versions. Conditional central entries keep the analyzer
test project's Roslyn 4.8 baseline separate from the solution-workspace packages.
Core has no package references, and central transitive pinning is disabled.

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
- Until [F23](https://github.com/kbourzayq/arch-guard/issues/23) is delivered,
  `analyze_changes` is explicitly `unsupported`, with no evaluated gate or counts.
  It cannot satisfy release validation. Keep the full scan mandatory.
- **F23 follow-up:** when comparison is implemented, update the correction test's
  unsupported assertion and this checklist together. Require CLI `scan --base` or
  MCP `analyze_changes` against an explicit release base, covering staged,
  unstaged and untracked working-tree changes with full base/candidate architecture
  contexts. Require completed coverage and a passed gate; missing refs, failed
  snapshot loads and unsupported results must block comparison approval. Retain
  full-scan validation and normal CI.

## Report a bug

Open an [issue](https://github.com/kbourzayq/arch-guard/issues/new) with:

- The tool revision/version, operating system and relevant SDK versions.
- The exact command or MCP tool arguments, configuration and target framework.
- Expected behavior, actual output and exit code or MCP status.
- A small reproducer and relevant policy, with private source, paths and secrets removed.

Distinguish an architecture finding from an operational coverage issue. Both matter,
but an empty incomplete result is not evidence that a repository is compliant.
