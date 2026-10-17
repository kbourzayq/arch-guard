# Architecture Guardian for .NET

Architecture Guardian is an offline, deterministic architecture policy and validation
layer for .NET teams and coding agents.

This repository currently implements [F01](https://github.com/kbourzayq/arch-guard/issues/1):
the .NET 10 solution, component boundaries, architecture checks and CI. Policy models,
solution loading, rules and analysis commands are subsequent features in the
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44).

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
Windows and macOS for pull requests and pushes to `main`. The tests check evaluated
MSBuild references in both Debug and Release, solution membership, Core's independence
from external packages and adapters, and CLI process behavior. Warnings fail the build.

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
| `ArchitectureGuardian.Core` | Framework-neutral policy, graph and rule evaluation contracts and logic, added in later features | None |
| `ArchitectureGuardian.Analysis` | Solution-wide application services and source/workspace integration, added in later features | Core |
| `ArchitectureGuardian.Cli` | Console entry point and composition | Analysis |

Core and Analysis are intentionally empty libraries at this stage. Adapters for
compiler analyzers, MCP, configuration and reporting will be added when their
features need them. [ADR 0001](docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md)
records ports, dependency direction, the two analysis engines and compiler-host
compatibility constraints.
