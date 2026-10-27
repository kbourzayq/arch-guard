# Workspace loading

CLI and MCP use `MSBuildWorkspaceLoader` to load C# SDK projects from `.csproj`,
`.sln` or `.slnx` inputs. Referenced projects load from source, including projects
outside the solution when referenced by its members. Unsupported projects and empty
solutions cannot establish complete coverage.

This reference covers build context, trust and the reusable loading API. Start with
[installation](installation.md) and the [CLI guide](cli.md) for command-line use.
The same loading service feeds [repository normalization](repository-model.md),
[dependency extraction](dependency-graph.md) and solution rule evaluation.
The command examples below run from the source checkout root.

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

MCP's operator-selected workspace root limits tool input paths; it does not restrict
what trusted tasks or generators can read, write or execute. Treat CLI, MCP and
compiler-hosted generators as code running with their process's permissions.
Do not mount a real home directory, credential store, SSH agent or shared writable
package cache into an untrusted build. A dedicated disposable cache prevents that
build from modifying packages later consumed by a trusted environment.

## Offline and deterministic operation

Policy parsing, compiled rule metadata and neutral graph/rule evaluation run locally.
The tool does not download rule implementations, query a remote service, fetch help
pages or restore during a full `scan`, `load` or `inspect`. Opening a documentation
URL is a separate browser action. Editor schema extensions may fetch schemas on
their own; the repository's local schema is available without that step.

Prepare the selected context before disconnecting:

1. Install the SDK selected by the input's `global.json`, its reference/workload
   packs and any required generator/build tools. Preserve pinned dependency versions
   and committed lock files.
2. Explicitly restore the complete input using the intended configuration/framework.
   When lock files are maintained, use `dotnet restore <input> --locked-mode`
   with those same MSBuild properties. Verify restore succeeds before scanning.
3. Keep the source/policy snapshot, SDK, package assets, generator inputs and build
   properties fixed. Run separately for every configuration/framework you require.
4. Enforce offline execution with the container/VM network boundary. Restore may
   need approved feeds during preparation; repository-controlled tasks, SDK resolvers
   and generators may attempt network access even when the tool itself does not.

For the same prepared semantic context and policy, rules produce the same diagnostic
identities, evidence ordering and gate decision. Authored paths are policy-relative;
generated paths use generator/type/hint identities rather than physical output
directories. See [repository identities](repository-model.md#identities-and-reproducibility)
and [graph ordering](dependency-graph.md#stable-identities-and-ordering).
Changes to tool/SDK/package versions, conditional sources or generated content can
change the context. Arbitrary build tasks and generators can depend on time, network,
environment variables or local files; deterministic evaluation does not neutralize
those inputs. Operational SDK/compiler messages may contain machine-specific paths.

`scan --base` and MCP `analyze_changes` additionally need the explicit Git base tree
and both trees' SDK/package requirements available locally. They perform a bounded
cache-only locked restore in temporary snapshots; they never fetch history or
download packages. Missing cache entries or unsupported snapshot inputs fail coverage.
See [change preparation](changes.md#snapshot-preparation-and-trust). Temporary copies
and cache-only restore do not sandbox repository-controlled execution.

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
references and normalized model identity checks. The generator fixture has its own locked restore
and builds once through a build-only reference from the test project. Integration tests
copy inputs to isolated temporary directories and stage generator DLLs with test
build artifacts without rebuilding them per class: Roslyn's process-lifetime loader can keep those DLLs locked on
Windows. Fixture restores serialize project restoration, including relocated inputs,
to avoid competing writes through temporary-directory path aliases. The loader
validates the current evaluated snapshot; it does not guarantee
that restore assets are fresh after arbitrary repository edits.
