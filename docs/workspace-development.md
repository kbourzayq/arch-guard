# Workspace implementation and validation

## Roslyn and MSBuild responsibilities

Analysis uses Roslyn's `Microsoft.CodeAnalysis.Workspaces.MSBuild` and
`Microsoft.CodeAnalysis.CSharp.Workspaces` packages (both 5.9.0). `MSBuildWorkspace`
opens solutions/projects; `Project.GetCompilationAsync` supplies C# compilations,
semantic context and compiler diagnostics. MSBuild evaluates configuration,
frameworks, source inclusion and references. An additional check compares evaluated
assembly references with the compilation so unused missing references also prevent
completion. This evaluation carries the opened workspace's MSBuild globals, including
`SolutionDir` for solution inputs, and matches the pinned build host's design-time
defaults. Loading an individual project does not invent a solution directory.

Generator-reported errors are collected separately from compiler diagnostics.
Roslyn 5.9 keeps its workspace generator diagnostics API internal, so the adapter
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


## Regression coverage

The fixtures under `tests/Fixtures/Workspaces` cover the three input formats,
project references, conditional source inclusion and compilation, two frameworks,
missing SDKs/references, solution-conditioned references, generator diagnostics and
failed restore. The `ModularMonolith` fixture adds multi-project modules, contract
references and normalized model identity checks. The generator fixture has its own locked restore
and builds once through a build-only reference from the test project. Integration tests
copy inputs to isolated temporary directories and stage generator DLLs with test
build artifacts without rebuilding them per class: Roslyn's process-lifetime loader can keep those DLLs locked on
Windows. Fixture restores serialize project restoration, including relocated inputs,
to avoid competing writes through temporary-directory path aliases. The loader
validates the current evaluated snapshot; it does not guarantee
that restore assets are fresh after arbitrary repository edits.
