# Architecture Guardian for .NET

Architecture Guardian is an offline, deterministic architecture policy and validation
layer for .NET teams and coding agents.

This repository implements [F01](https://github.com/kbourzayq/arch-guard/issues/1) and
[F02](https://github.com/kbourzayq/arch-guard/issues/2): the .NET 10 solution, component
boundaries, CI, shared rule/diagnostic/result contracts and reusable analysis
orchestration. YAML policy, solution loading, rule implementations and analysis
commands are subsequent features in the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44).

## Architecture and flows

The [architecture overview](docs/architecture/overview.md) explains component
responsibilities, dependency direction and the planned solution/compiler paths.
Mermaid diagrams show the [service execution](docs/shared-contracts.md#service-execution-flow),
[coverage outcomes](docs/shared-contracts.md#outcome-flow) and
[catalog-to-documentation flow](docs/rule-catalog.md#how-catalog-metadata-reaches-a-diagnostic).
The overview distinguishes the implemented F02 service from future integrations.

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
identity and ordering, severity policy, analysis outcomes and cancellation, plus evaluated
MSBuild references in both Debug and Release, solution membership, Core's independence
from external packages and adapters, and CLI process behavior. Warnings fail the build.
Builds emit XML API documentation; missing documentation for public types or members
also fails the build.

NuGet uses the public nuget.org source. Dependency lock files are committed. When
intentionally changing dependencies, run `dotnet restore ArchitectureGuardian.slnx
--force-evaluate`, review the lock-file changes and rerun the checks above.

## Run the CLI scaffold

```sh
dotnet run --project src/ArchitectureGuardian.Cli -- --help
dotnet run --project src/ArchitectureGuardian.Cli -- --version
```

No arguments also prints help. Unsupported commands or arguments return exit code 2
and write an error to stderr. Scanning is not implemented; `scan` returns 2 rather than
reporting a successful analysis. The `arch-guard` name in help is the intended command
name; distribution as a .NET tool is tracked separately in F27.

## Components

| Project | Responsibility | Direct production references |
| --- | --- | --- |
| `ArchitectureGuardian.Core` | Neutral rule, diagnostic, severity policy and analysis contracts; canonical rule catalog | None |
| `ArchitectureGuardian.Analysis` | Reusable orchestration over prepared contexts; source/workspace integration follows in F04 | Core |
| `ArchitectureGuardian.Cli` | Console entry point and composition | Analysis |

Rules return evidence-backed findings without severity. The analysis service applies
severity policy and returns `Completed`, `Incomplete`, `Failed` or `Cancelled`,
preserving partial findings. Completion describes coverage, independently of quality
gate decisions. No registered rules produces an incomplete result.

See the [shared contracts guide](docs/shared-contracts.md) for composition, source
coordinates, deterministic identities and cancellation behavior, and the
[canonical rule catalog](docs/rule-catalog.md) for AG001–AG006. The catalog defines
identities; rule evaluation implementations are still pending.

Adapters for compiler analyzers, MCP, configuration and reporting will be added when
their features need them. [ADR 0001](docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md)
records ports, dependency direction, the two analysis engines and compiler-host
compatibility constraints.
