# Architecture Guardian for .NET

Architecture Guardian is an offline, deterministic architecture policy and validation
layer for .NET teams and coding agents.

This repository implements [F01](https://github.com/kbourzayq/arch-guard/issues/1),
[F02](https://github.com/kbourzayq/arch-guard/issues/2),
[F03](https://github.com/kbourzayq/arch-guard/issues/3) and
[F04](https://github.com/kbourzayq/arch-guard/issues/4): the .NET 10 solution, component
boundaries, CI, shared contracts, reusable analysis orchestration, versioned YAML
policy, deterministic project matching, severity gates and trusted workspace loading.
Repository normalization, rule implementations and scanning are subsequent features in the
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

The GitHub Actions [CI workflow](.github/workflows/ci.yml) runs these checks on Linux,
Windows and macOS for pull requests and pushes to `main`. Tests cover diagnostic
identity and ordering, YAML validation, policy references, portable globs, inventory
resolution, rule selection, severity gates, analysis outcomes and cancellation, plus
workspace loading for all three input formats, framework selection, conditional
compilation, solution-conditioned references, source-generator diagnostics, missing
SDKs/references, failed restores and CLI loading exit codes.
They also cover evaluated MSBuild references in both Debug and Release, solution membership, Core's independence
from external packages and adapters, and CLI process behavior. Warnings fail the build.
Builds emit XML API documentation; missing documentation for public types or members
also fails the build.

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

## Components

| Project | Responsibility | Direct production references |
| --- | --- | --- |
| `ArchitectureGuardian.Core` | Neutral contracts, canonical rule catalog, immutable architecture policy, project mapping, dependency permissions and gates | None |
| `ArchitectureGuardian.Analysis` | Reusable orchestration and trusted Roslyn/MSBuild workspace loading | Core |
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
identities; rule evaluation implementations are still pending.

Adapters for compiler analyzers, MCP and reporting will be added when
their features need them. [ADR 0001](docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md)
records ports, dependency direction, the two analysis engines and compiler-host
compatibility constraints.
