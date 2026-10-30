# Workspace loading

CLI and MCP load a restored C# solution or project in one explicit build context.
Supported inputs are `.sln`, `.slnx` and `.csproj`. Referenced projects can load from
source even when they are outside a solution's explicit membership. Unsupported
projects and empty solutions cannot establish complete coverage.

Install the [CLI](install.md#install-the-cli). From your repository root, authorize
its build logic, then replace these input paths with your own:

```sh
dotnet restore MyApp.slnx -p:Configuration=Release
archguard load MyApp.slnx --trusted --configuration Release
archguard scan MyApp.slnx --policy architecture.guardian.yaml --trusted --configuration Release
```

`load` checks whether the requested workspace can be analyzed. It does not resolve
policy or evaluate architecture rules; a successful load is not a passing scan.
Use `inspect` for ownership and `scan` for selected rules and the severity gate.
See [dependency analysis](concepts/dependency-analysis.md) for the observed model.

## Roslyn and MSBuild responsibilities

MSBuild evaluates source inclusion, references, configuration and framework. Roslyn
provides the resulting C# compilation, bound symbols and compiler diagnostics.
Workspace validation checks references and generator-reported errors as well as
bound code, so an unused missing reference can still prevent completion.

Source generators can run during compilation and again in a validation pass.
Generator errors/exceptions prevent complete analysis; ordinary generator warnings
alone do not. Supply their dependencies, AdditionalFiles and options in the chosen
context. The tool does not run normal package restore or a full build for you.

SDK discovery starts from the input directory and honors its applicable
`global.json`. It does not require the SDK used to develop Architecture Guardian.
For solution input, evaluated build context includes its solution directory;
loading a standalone project does not invent one.

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
directories. See [dependency identities and evidence](concepts/dependency-analysis.md#stable-identities-and-ordering).
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

| Outcome | Loading status | CLI exit |
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

The loader validates the current evaluated snapshot; it cannot guarantee that
restore assets remain fresh after edits. Restore again when dependencies or build
inputs change, then rerun the affected configuration/framework scans.
