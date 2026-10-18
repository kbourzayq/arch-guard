# Workspace loading

F04 adds reusable `MSBuildWorkspaceLoader` integration in Analysis and a `load`
command for validating workspace context. It supports C# SDK projects supplied as
`.csproj`, `.sln` or `.slnx`. Referenced projects load from source, including projects
outside the solution when referenced by its members. Unsupported projects and empty
solutions cannot establish complete coverage. F05 adds the separate
[repository normalization API](repository-model.md), and F06 adds the
[dependency evidence graph](dependency-graph.md). F07 adds
[AG001 evaluation](rule-catalog.md#ag001-module-dependencies). Remaining rules and
CLI scanning follow in F08–F10.

```sh
dotnet restore path/to/Application.slnx
dotnet run --project src/ArchitectureGuardian.Cli -- load path/to/Application.slnx --trusted
dotnet run --project src/ArchitectureGuardian.Cli -- load path/to/Application.csproj --trusted --configuration Release --framework net10.0
```

The API accepts `WorkspaceLoadRequest` and returns an owned `WorkspaceLoadResult`.
Dispose the result after consuming its Roslyn `Solution`. Roslyn objects remain in
Analysis; `RepositoryModelBuilder` converts them into neutral Core inputs.
`WorkspaceLoadResult.InputPath` retains the original absolute input so the builder
can preserve its policy-relative identity. Loading
coverage uses Core's status and operational-issue contracts but is separate from rule
evaluation: a completed load does not mean architecture policy passed.

## Roslyn and MSBuild responsibilities

Analysis uses Roslyn's `Microsoft.CodeAnalysis.Workspaces.MSBuild` and
`Microsoft.CodeAnalysis.CSharp.Workspaces` packages (both 5.0.0). `MSBuildWorkspace`
opens solutions/projects; `Project.GetCompilationAsync` supplies C# compilations,
semantic context and compiler diagnostics. MSBuild evaluates configuration,
frameworks, source inclusion and references. An additional check compares evaluated
assembly references with the compilation so unused missing references also prevent
completion. This evaluation carries the opened workspace's MSBuild globals, including
`SolutionDir` for solution inputs, and matches the pinned build host's design-time
defaults. Loading an individual project does not invent a solution directory.

Generator-reported errors are collected separately from compiler diagnostics.
Roslyn 5.0 keeps its workspace generator diagnostics API internal, so the adapter
runs an additional validation pass through the public C# generator driver with the
original syntax trees, parse options, additional files and analyzer configuration.
Previously generated trees are excluded from this pass. Generators may execute again;
reported errors and generator exceptions make loading incomplete. Ordinary generator
warnings alone do not prevent completion.

SDK discovery first runs `dotnet --version` from the input directory, capturing both
output streams. `Microsoft.Build.Locator` 1.11.2 then registers that exact SDK before
the adapter loads MSBuild assemblies. The dependency choices and runtime exclusions
are recorded in [ADR 0001](architecture/decisions/0001-component-boundaries-and-analysis-engines.md).
Core has no Roslyn or MSBuild package references.

```csharp
using ArchitectureGuardian.Analysis.Workspaces;

using var result = await new MSBuildWorkspaceLoader().LoadAsync(
    new WorkspaceLoadRequest(inputPath, isTrusted: true,
        configuration: "Release", targetFramework: "net10.0"),
    cancellationToken);
```

Inspect `result.Status` and `result.Issues` before consuming `result.Solution`.
The solution can be partial or null when loading does not complete.

## Trust and isolation

**MSBuild evaluation and design-time builds may execute repository-controlled tasks,
SDK resolvers and imported build logic.** Even restore can execute build logic.
Loading requires an explicit caller trust decision (`isTrusted: true` or `--trusted`)
before SDK discovery or workspace creation. This flag grants permission to execute
that logic; it does not provide isolation.

For an untrusted repository, perform both restore and loading in a disposable
container or VM with no credentials, host home-directory mounts, Docker socket or
other privileged services. Give it a writable copy of the source, temporary output
directories and a dedicated package cache. Limit network access to approved feeds
for restore, then disable it for loading. Apply CPU, memory and process deadlines at
the isolation boundary and discard the environment afterward. Never trust a repository
merely because the intended operation is read-only.

The loader does not invoke restore or a normal build. Design-time MSBuild can write
intermediate files or execute custom tasks, and compilation can run source generators.
The host must terminate the isolated process if custom build logic ignores cancellation.

## Configuration, frameworks and SDKs

- Configuration defaults to `Debug`. `--configuration` sets the MSBuild global
  property, so conditional references, source inclusion and compilation constants
  follow the evaluated project. Inactive preprocessor branches are outside that run.
- Single-target projects load their declared framework by default. If any project
  declares multiple `TargetFrameworks`, omitting `--framework` yields incomplete
  coverage with `framework-required`, even if Roslyn selected an initial target.
- `--framework` selects one framework for every project in the loaded context. Each
  project must declare that framework; otherwise `framework-unavailable` prevents
  completion. Run once per declared framework to cover a multi-targeted repository.
  Mixed-framework solutions may need separate project inputs; a single load never
  claims coverage of other frameworks or configurations.
- SDK discovery starts at the input directory and honors its applicable `global.json`.
  MSBuild registration is process-wide and serialized. A subsequent request requiring
  another SDK must use a new process. A host that has already registered MSBuild must
  use a dedicated loading process, avoiding an unverifiable SDK choice.

## Coverage and errors

| Outcome | API status | CLI exit |
| --- | --- | --- |
| All requested context available, no workspace issues, compiler/generator errors or generator failures | `Completed` | `0` |
| Untrusted/unsupported/missing input, missing SDK or restore assets, failed restore, unavailable reference, compiler/generator error, generator failure, workspace warning/failure or framework mismatch | `Incomplete` | `2` |
| Requested cancellation before or during opening/compilation | `Cancelled` | `2` |

`load` reports success on stdout and errors on stderr. Ctrl+C requests cooperative
cancellation. Partial projects and available issues survive incomplete/cancelled
loading, but callers must inspect status before evaluating or publishing results.
Operational issues have stable category codes and deterministic ordering; build-tool
messages retain their original SDK and filesystem context.

Restore each input successfully before loading. Missing `project.assets.json` produces
`restore-required`. A failed restore, missing reference or design-time build failure
also produces the underlying workspace/compiler diagnostics. No implicit network
restore hides failures. Compiler errors conservatively prevent complete coverage,
including unresolved types and metadata, rather than silently reducing semantic facts.

The fixtures under `tests/Fixtures/Workspaces` cover the three input formats,
project references, conditional source inclusion and compilation, two frameworks,
missing SDKs/references, solution-conditioned references, generator diagnostics and
failed restore. The `ModularMonolith` fixture adds multi-project modules, contract
references and normalized model identity checks. The generator fixture has its own locked restore. Integration tests
copy inputs to isolated temporary directories and stage generator DLLs with test
build artifacts: Roslyn's process-lifetime loader can keep those DLLs locked on
Windows. Fixture restores serialize project restoration, including relocated inputs,
to avoid competing writes through temporary-directory path aliases. The loader
validates the current evaluated snapshot; it does not guarantee
that restore assets are fresh after arbitrary repository edits.
