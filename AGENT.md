# Coding rules

These rules apply to the whole repository. Read [README.md](README.md) and
[ADR 0001](docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md)
before changing component responsibilities or dependencies.

## Scope and code discovery

- Implement the requested feature and its acceptance criteria. Keep changes focused;
  introduce adapters and abstractions when a concrete use case requires them.
- Inspect existing code and tests before adding another implementation. Preserve
  unrelated work in the checkout.
- If a `.codegraph/` directory exists at the repository root, use CodeGraph before
  text searches or file reads to locate or understand code. Prefer the
  `codegraph_explore` MCP tool, or `codegraph explore "<question or symbol>"`.
  If no index exists, use ordinary tools; do not create an index unless requested.

## Architecture boundaries

- Keep project dependencies pointing inward: **CLI → Analysis → Core**.
- Core contains neutral contracts and pure policy, graph and rule logic. It has no
  project, package or explicit assembly dependencies beyond the .NET base library.
  Keep Roslyn, MSBuild, file/network/process access, CLI, MCP, serializers and report
  formatting outside Core.
- Analysis owns solution-wide orchestration and workspace integration. Convert host
  objects into Core data at adapter boundaries; do not expose Roslyn or transport
  types through Core contracts.
- CLI handles arguments, composition, console output and exit-code mapping. Keep
  application services reusable by other hosts and rule evaluation inside Core.
- Keep solution-wide and compiler-hosted analysis separate. A compiler adapter must
  use its host's compilation context, without opening an MSBuildWorkspace, restoring
  projects or launching CLI. Do not claim whole-solution coverage from one compilation.
- Add production projects to the solution and explicitly update the dependency
  boundary tests and architecture decision when introducing a new boundary.

## C# conventions

- Use the SDK selected by `global.json`, the shared settings in
  `Directory.Build.props` and the formatting in `.editorconfig`. Keep nullable
  reference types enabled and builds free of warnings.
- Use file-scoped namespaces and explicit accessibility. Use PascalCase for types
  and public members, camelCase for parameters and locals, and `_camelCase` for
  private instance fields. Prefix interfaces with `I`.
- Group types by scope and responsibility, with namespaces matching the project
  root namespace plus the relative folder path. Use plural nouns for groups such
  as `Policies`, `Rules` and `Diagnostics`. Keep each top-level type in its own
  matching file; nested implementation helpers may stay with their owning type.
  Keep the CLI entry point at the project root.
- Prefer small, cohesive types and straightforward control flow. Use immutable
  records for value-like contracts where appropriate; avoid exposing mutable
  collections that callers could use to corrupt evaluation inputs or results.
- Validate external input at adapter boundaries. Express optional values with
  nullable types; use the null-forgiving operator only for an established invariant.
- Use asynchronous APIs for asynchronous work, with an `Async` suffix and a final
  `CancellationToken` parameter where cancellation applies. Forward cancellation
  and avoid `.Result`, `.Wait()` and `async void` application methods.
- Comments should explain intent, invariants or tradeoffs. Keep technology details
  out of domain contracts and user-facing messages unless they help resolve a problem.
- Add XML documentation to every class and public member, including constructors.
  Explain responsibilities, inputs, results and relevant failure/cancellation behavior.
  Use implementation comments for important branches or invariants; avoid narrating obvious code.

## Analysis correctness

- Produce the same diagnostic ordering, identities and evidence for the same input
  snapshot and policy. Use explicit ordering and ordinal comparisons for identifiers;
  do not depend on filesystem enumeration order or the current culture.
- Keep rule evaluation separate from severity policy, quality gates and rendering.
  Preserve rule IDs from the canonical catalog in the project roadmap.
- Distinguish completed, incomplete, failed and cancelled work. Never turn a loading
  failure, unsupported command or missing context into a successful clean analysis.
- Preserve useful diagnostic context. Do not swallow exceptions or cancellation to
  continue with silently reduced analysis coverage.
- Keep normal output separate from errors. CLI operational errors go to stderr; protocol
  adapters reserve stdout for protocol messages and send logs to stderr.

## Dependencies and validation

- Reuse the base library and existing dependencies when they meet the need. Explain
  new packages in the change description and keep them in the responsible adapter.
- Commit dependency lock files. For intentional dependency changes, run
  `dotnet restore ArchitectureGuardian.slnx --force-evaluate` and review the changes.
  Do not bypass locked restore, warning checks or boundary tests to make CI pass.
- Add focused xUnit tests for changed behavior, including relevant failure and
  cancellation paths. Verify observable outcomes and dependency constraints rather
  than duplicating implementation details in tests.
- For code, project or dependency changes, run the CI checks from the repository root:

  ```sh
  dotnet restore ArchitectureGuardian.slnx --locked-mode
  dotnet build ArchitectureGuardian.slnx --configuration Release --no-restore
  dotnet format ArchitectureGuardian.slnx --verify-no-changes --no-restore
  dotnet test ArchitectureGuardian.slnx --configuration Release --no-build --no-restore -m:1
  ```

- Keep changes compatible with Linux, Windows and macOS. Use path APIs and portable
  process arguments instead of OS-specific path concatenation or shell assumptions.
- For documentation-only changes, check accuracy, links and formatting; runtime
  tests are unnecessary unless the documented behavior also changes.
- Update relevant documentation when commands, contracts or architecture decisions
  change. Report what changed, what was verified and any checks that could not run;
  distinguish local verification from an actual GitHub Actions run.
