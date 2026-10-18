# Architecture Guardian for .NET

Architecture Guardian is an offline, deterministic architecture policy and validation
layer for .NET teams and coding agents.

This repository implements [F01](https://github.com/kbourzayq/arch-guard/issues/1),
[F02](https://github.com/kbourzayq/arch-guard/issues/2),
[F03](https://github.com/kbourzayq/arch-guard/issues/3),
[F04](https://github.com/kbourzayq/arch-guard/issues/4),
[F05](https://github.com/kbourzayq/arch-guard/issues/5),
[F06](https://github.com/kbourzayq/arch-guard/issues/6),
[F07](https://github.com/kbourzayq/arch-guard/issues/7) and
[F08](https://github.com/kbourzayq/arch-guard/issues/8): the .NET 10 solution, component
boundaries, CI, shared contracts, reusable analysis orchestration, versioned YAML
policy, deterministic project matching, severity gates, trusted workspace loading,
a normalized repository/module model, dependency extraction with an evidence graph,
AG001 enforcement of module and public-contract permissions, and AG002 enforcement
of the configured layer matrix.
Remaining rules and CLI scanning follow in the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44).

## Architecture and flows

The [architecture overview](docs/architecture/overview.md) explains component
responsibilities, dependency direction and the planned solution/compiler paths.
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

The [repository policy](architecture.guardian.yaml) maps all six solution projects.
A [modular-monolith example](docs/examples/modular-monolith/architecture.guardian.yaml)
shows contract-only permissions. These files can be loaded through `YamlPolicyLoader`
and resolved against a supplied inventory; CLI policy loading and scanning follow in F10.

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
project copies per test class, but batch their valid-project restores through
`tests/Fixtures/Workspaces/Preparation.slnx` to reduce process startup. Deliberately
broken and unrestored fixtures are excluded from that preparation.

The GitHub Actions [CI workflow](.github/workflows/ci.yml) runs these checks on Linux,
Windows and macOS for pull requests and pushes to `main`. Tests cover diagnostic
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

AG001/AG002 tests cover approved contracts, configured layers, seeded forbidden
references and source evidence, same-module and same-layer dependencies, direct directional permissions, rule selection, reporting
severity, gates, deterministic findings and preservation of incomplete coverage.

NuGet uses the public nuget.org source. Dependency lock files are committed. When
intentionally changing dependencies, run `dotnet restore ArchitectureGuardian.slnx
--force-evaluate`, review the lock-file changes and rerun the checks above.

## Run the CLI

```sh
dotnet run --project src/ArchitectureGuardian.Cli -- --help
dotnet run --project src/ArchitectureGuardian.Cli -- --version
dotnet run --project src/ArchitectureGuardian.Cli -- load path/to/Application.slnx --trusted
```

No arguments also prints help. Unsupported commands or arguments return exit code 2
and write an error to stderr. Scanning is not implemented; `scan` returns 2 rather than
reporting a successful analysis. The `arch-guard` name in help is the intended command
name; distribution as a .NET tool is tracked separately in F27.

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
| `ArchitectureGuardian.Core` | Neutral contracts, canonical rule catalog, immutable architecture policy, project mapping, dependency permissions, AG001/AG002 evaluation, gates, repository data and evidence graphs | None |
| `ArchitectureGuardian.Analysis` | Reusable orchestration, trusted Roslyn/MSBuild workspace loading, repository normalization and dependency extraction | Core |
| `ArchitectureGuardian.Configuration` | Strict v1 YAML loading and actionable validation errors; YamlDotNet stays in this adapter | Core |
| `ArchitectureGuardian.Cli` | Console entry point and composition | Analysis |

Rules return evidence-backed findings without severity. The analysis service applies
severity policy and returns `Completed`, `Incomplete`, `Failed` or `Cancelled`,
preserving partial findings. Completion describes coverage, independently of quality
gate decisions. No registered rules produces an incomplete result. Policy selection
rejects missing selected implementations before analysis, and severity gates reject
incomplete, failed or cancelled results regardless of their diagnostic count.

See the [shared contracts guide](docs/shared-contracts.md) for composition, source
coordinates, deterministic identities and cancellation behavior, and the
[canonical rule catalog](docs/rule-catalog.md) for AG001–AG006. The catalog defines
identities. `ModuleDependenciesRule` implements AG001 and `LayerDependenciesRule`
implements AG002 over `DependencyBuildResult`; AG003–AG006 remain planned. The
[AG001 guide](docs/rule-catalog.md#ag001-module-dependencies),
[AG002 guide](docs/rule-catalog.md#ag002-layer-dependencies) and
[policy composition example](docs/policy.md#host-composition) explain their scope and use.

Adapters for compiler analyzers, MCP and reporting will be added when
their features need them. [ADR 0001](docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md)
records ports, dependency direction, the two analysis engines and compiler-host
compatibility constraints.
