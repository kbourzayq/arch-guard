# Architecture Guardian for .NET

Architecture Guardian is an offline, deterministic architecture policy and validation
layer for .NET teams and coding agents.

This repository implements [F01](https://github.com/kbourzayq/arch-guard/issues/1),
[F02](https://github.com/kbourzayq/arch-guard/issues/2),
[F03](https://github.com/kbourzayq/arch-guard/issues/3),
[F04](https://github.com/kbourzayq/arch-guard/issues/4),
[F05](https://github.com/kbourzayq/arch-guard/issues/5),
[F06](https://github.com/kbourzayq/arch-guard/issues/6),
[F07](https://github.com/kbourzayq/arch-guard/issues/7),
[F08](https://github.com/kbourzayq/arch-guard/issues/8),
[F09](https://github.com/kbourzayq/arch-guard/issues/9),
[F10](https://github.com/kbourzayq/arch-guard/issues/10),
[F11](https://github.com/kbourzayq/arch-guard/issues/11),
[F12](https://github.com/kbourzayq/arch-guard/issues/12),
[F13](https://github.com/kbourzayq/arch-guard/issues/13),
[F14](https://github.com/kbourzayq/arch-guard/issues/14),
[F15](https://github.com/kbourzayq/arch-guard/issues/15),
[F16](https://github.com/kbourzayq/arch-guard/issues/16) and
[F17](https://github.com/kbourzayq/arch-guard/issues/17): the .NET 10 solution, component
boundaries, CI, shared contracts, reusable analysis orchestration, versioned YAML
policy, deterministic project matching, severity gates, trusted workspace loading,
a normalized repository/module model, dependency extraction with an evidence graph,
AG001 enforcement of module and public-contract permissions, and AG002 enforcement
of the configured layer matrix, AG003 module cycle detection, and CLI scan, inspect
and explain commands, with compliant and deliberately violating reference solutions,
a compiler-hosted AG001/AG002/AG004/AG005/AG006 analyzer with a semantic test harness, and
AG004 semantic enforcement of declared implementation boundaries and AG005 explicit
assembly/namespace restrictions and AG006 layer-scoped domain technology restrictions
with namespace exceptions in both engines, plus labeled semantic accuracy and engine
parity fixtures and a local read-only MCP stdio host.
Additional capabilities follow in the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44).

## Architecture and flows

The [architecture overview](docs/architecture/overview.md) explains component
responsibilities, dependency direction and the solution/compiler paths.
Mermaid diagrams show the [service execution](docs/shared-contracts.md#service-execution-flow),
[coverage outcomes](docs/shared-contracts.md#outcome-flow) and
[catalog-to-documentation flow](docs/rule-catalog.md#how-catalog-metadata-reaches-a-diagnostic).
The overview distinguishes the implemented service and policy APIs from future integrations.
Its [source organization guide](docs/architecture/overview.md#source-organization)
lists responsibility folders and their matching namespaces across the solution.

## Architecture policy

The [policy guide](docs/policy.md) defines `architecture.guardian.yaml` v1, including
project identities, globs, module/layer ownership, public contracts, direct dependency
permissions, selected rule IDs, severities and gates. The [JSON Schema](docs/schema/architecture.guardian.v1.schema.json)
supports editor validation; the loader additionally checks references and the
resolver rejects unmatched or ambiguously mapped projects.

The [repository policy](architecture.guardian.yaml) maps all nine solution projects.
A [modular-monolith example](docs/examples/modular-monolith/architecture.guardian.yaml)
shows contract-only permissions. These files can be loaded through `YamlPolicyLoader`
and resolved against a supplied inventory or used directly by CLI `scan` and `inspect`.

## Build and check

Install the .NET SDK specified in [global.json](global.json). Stable patches within
that SDK feature band are allowed; previews are excluded. Run from the repository root:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
dotnet format ArchitectureGuardian.slnx --verify-no-changes --no-restore
dotnet test ArchitectureGuardian.slnx --configuration Release --no-build --no-restore
```

For fast feedback after a build, run the unit tests without MSBuild integration:

```sh
dotnet test tests/ArchitectureGuardian.Tests/ArchitectureGuardian.Tests.csproj --configuration Release --no-build --no-restore --filter "Category!=Integration"
```

Use `--filter "Category=Integration"` for the MSBuild/Roslyn workspace, CLI process
and evaluated dependency-boundary tests. The unfiltered command above runs both
categories and remains required before publishing. Integration fixtures keep separate
project copies per test class, restoring only each suite's required contexts through
`Preparation.slnx`, `SemanticPreparation.slnx` or the modular-monolith solution under
`tests/Fixtures/Workspaces`. The generator builds once as a build-only test project
dependency; fixture setup stages its binary instead of rebuilding it for every class.
Deliberately broken and unrestored fixtures are excluded from preparation.
Repeated CLI policy permutations run through the real command pipeline in process;
representative scan/inspect outcomes, reference solutions, SDK failures and smoke
checks still exercise separate CLI processes.

The GitHub Actions [CI workflow](.github/workflows/ci.yml) runs these checks on Linux,
Windows and macOS for pull requests and pushes to `main`, uploading TRX timings for
each OS. A separate Linux job builds the analyzer package once, so compiler-host
checks can start without waiting for the slowest OS test job. All validation jobs
remain required for a successful workflow. Tests cover diagnostic
identity and ordering, YAML validation, policy references, portable globs, inventory
resolution, rule selection, severity gates, analysis outcomes and cancellation,
normalized source declarations, multi-project modules, contract membership and
checkout-independent model identities.
Dependency tests cover project/assembly/namespace/type edges, module/layer aggregation,
traceable forbidden references, semantic forms, generated-source evidence, deterministic
traversal/cycles, relocation and incomplete semantic context. Workspace tests cover all three input formats,
framework selection, conditional
compilation, solution-conditioned references, source-generator diagnostics, missing
SDKs/references, failed restores and CLI loading exit codes.
They also cover evaluated MSBuild references in both Debug and Release, solution membership, Core's independence
from external packages and adapters, and CLI process behavior. Warnings fail the build.
Builds emit XML API documentation; missing documentation for public types or members
also fails the build.

AG006 tests cover configured layer membership, technology identities, scoped namespace
exceptions, independent overlapping rules, evidence deduplication and partial coverage.
The [semantic accuracy guide](docs/semantic-accuracy.md) records exactly which rules run
in each engine, per-rule precision and limits, and reproducible parity checks across
frameworks/configurations, generated sources and unavailable references.
AG005 tests cover module/layer restrictions, framework/external assemblies, namespace
descendants, aliases/global usings, lookalikes, generated sources and cross-engine meanings.
AG004 tests cover public implementation types, contract and same-module access,
aliases (including generic/array aliases), partial/generated sources and evidence retention.
AG001–AG003 tests cover approved contracts, configured layers, seeded forbidden
references and source evidence, same-module and same-layer dependencies, direct directional permissions, rule selection, reporting
severity, gates, deterministic findings and preservation of incomplete coverage.
Cycle fixtures cover two-module, longer and disconnected cycles, repeated edges,
shortest representative paths, self-dependencies and allowed contract edges.

The [M1 demo](docs/m1-demo.md) provides runnable Clean Architecture and modular-monolith
reference solutions, exact gate outcomes, and the labeled AG001–AG003 regression set.

NuGet uses the public nuget.org source. Dependency lock files are committed. When
intentionally changing dependencies, run `dotnet restore ArchitectureGuardian.slnx
--force-evaluate`, review the lock-file changes and rerun the checks above.

## Run the CLI

```sh
dotnet run --project src/ArchitectureGuardian.Cli -- --help
dotnet run --project src/ArchitectureGuardian.Cli -- --version
dotnet run --project src/ArchitectureGuardian.Cli -- scan path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted
dotnet run --project src/ArchitectureGuardian.Cli -- inspect path/to/Application.slnx --policy path/to/architecture.guardian.yaml --trusted
dotnet run --project src/ArchitectureGuardian.Cli -- explain AG001
dotnet run --project src/ArchitectureGuardian.Cli -- load path/to/Application.slnx --trusted
```

No arguments also prints help. System.CommandLine supplies command-specific help,
argument validation and cancellation. The intended tool name is `archguard`; until
F27 packages it, use `dotnet run` as above (generated usage shows the assembly name).
See the [CLI guide](docs/cli.md) for output, policy paths and examples.

`scan` evaluates policy-selected AG001–AG006 rules and prints severity, rule ID,
source location, affected target, evidence, remediation and documentation. Exit 0
means completed and gate passed; 1 means completed and gate failed; 2 means invalid
input or unreliable, incomplete, failed or cancelled work. Operational errors go to
stderr; available findings remain on stdout. No gate result is claimed for incomplete
analysis. Selected rules without implementations fail explicitly.

`inspect` lists resolved modules, project/layer ownership, public contracts, direct
permissions, selected rule severities and the gate without evaluating rules.
`explain AG001` resolves case-sensitive canonical IDs AG001–AG006 locally, including all six implemented rules. `scan` and `inspect` require an explicit `--policy` path;
project identities are relative to its directory. Both accept `--configuration` and
`--framework` with the same scope and trust requirements as `load`.

The `load` command validates `.sln`, `.slnx` and `.csproj` workspace context after
an explicit restore. MSBuild may execute repository tasks, so trust is required.
Loading problems and cancellation return 2. See the [workspace loading guide](docs/workspace-loading.md)
for isolation, conditional compilation, framework selection and reusable API behavior.
The [repository model guide](docs/repository-model.md) describes normalization, stable
identities and module/contract/layer membership through `RepositoryModelBuilder`.
The [dependency graph guide](docs/dependency-graph.md) describes `DependencyGraphBuilder`,
origin evidence, deterministic traversal/cycles and extraction coverage.

## Components

| Project | Responsibility | Direct production references |
| --- | --- | --- |
| `ArchitectureGuardian.Core` | Neutral contracts, canonical rule catalog, immutable architecture policy, project mapping, dependency permissions, AG001–AG006 evaluation, gates, repository data and evidence graphs | None |
| `ArchitectureGuardian.Analysis` | Reusable orchestration, trusted Roslyn/MSBuild workspace loading, repository normalization and dependency extraction | Core |
| `ArchitectureGuardian.Configuration` | Strict v1 YAML loading and actionable validation errors; YamlDotNet stays in this adapter | Core |
| `ArchitectureGuardian.Analyzers` | netstandard2.0 C# compiler adapter; compilation-local AG001/AG002/AG004/AG005/AG006 and AdditionalFiles policy | None; source-links pure Core semantics |
| `ArchitectureGuardian.Cli` | System.CommandLine arguments, policy/service composition, diagnostics and exit codes | Analysis, Configuration |

Rules return evidence-backed findings without severity. The analysis service applies
severity policy and returns `Completed`, `Incomplete`, `Failed` or `Cancelled`,
preserving partial findings. Completion describes coverage, independently of quality
gate decisions. No registered rules produces an incomplete result. Policy selection
rejects missing selected implementations before analysis, and severity gates reject
incomplete, failed or cancelled results regardless of their diagnostic count.

See the [shared contracts guide](docs/shared-contracts.md) for composition, source
coordinates, deterministic identities and cancellation behavior, and the
[canonical rule catalog](docs/rule-catalog.md) for AG001–AG006. The catalog defines
identities. `ModuleDependenciesRule`, `LayerDependenciesRule`, `ModuleCyclesRule`,
`ImplementationBoundariesRule`, `ForbiddenReferencesRule` and `DomainInfrastructureRule`
implement AG001–AG006 over `DependencyBuildResult`. The
[AG001 guide](docs/rule-catalog.md#ag001-module-dependencies),
[AG002 guide](docs/rule-catalog.md#ag002-layer-dependencies),
[AG003 guide](docs/rule-catalog.md#ag003-module-cycles),
[AG004 guide](docs/rule-catalog.md#ag004-implementation-boundaries),
[AG005 guide](docs/rule-catalog.md#ag005-forbidden-references),
[AG006 guide](docs/rule-catalog.md#ag006-domain-infrastructure) and
[policy composition example](docs/policy.md#host-composition) explain their scope and use.

The [compiler analyzer guide](docs/compiler-analyzer.md) describes package consumption,
policy distribution, semantic scope and host validation. The MCP stdio adapter is implemented in F17;
MCP tools and reporting will follow in their respective features. [ADR 0001](docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md)
records ports, dependency direction, the two analysis engines and compiler-host
compatibility constraints.

## Run the MCP server

The [MCP guide](docs/mcp.md) covers the stdio executable, client configuration,
operator-selected workspace root, build trust, cancellation, limits and error contracts.
F17 provides protocol initialization and an empty tool catalog; F18 adds architecture tools.
