# ADR 0001: Component boundaries and two analysis engines

- Status: Accepted
- Date: 2026-10-17
- Delivery: [F01](https://github.com/kbourzayq/arch-guard/issues/1)

## Context

Architecture Guardian must evaluate architecture policies from a complete solution
and eventually provide compiler-hosted semantic diagnostics. CLI, MCP and reporting
protocols must not determine the policy model or rule semantics. A compiler invocation
does not have the same repository view or lifecycle as a standalone scan.

## Decision: dependencies point toward Core

Start with three .NET 10 projects and a separate architecture test project:

```text
ArchitectureGuardian.Cli --> ArchitectureGuardian.Analysis --> ArchitectureGuardian.Core
     console/composition        solution application services       pure policy/evaluation
```

An arrow means a project reference. CLI can consume Core types through Analysis's
transitive reference. Core has no project, package or explicit assembly references;
it uses only the .NET base class library. Core must never reference Roslyn, MSBuild,
CLI, MCP, serializers, reporting adapters or their transport/host types. It does not
load files, discover SDKs, invoke processes or write reports.

Analysis is the solution-wide engine and application-service implementation reusable
by CLI and the future MCP adapter. It references Core and will own workspace loading
and conversion of Roslyn/MSBuild data into neutral Core inputs. It may acquire those
external dependencies in F04. CLI composes the services, handles arguments and maps
results to console output and exit codes. It contains no rule evaluation.

Add configuration, reporting, compiler-analyzer and MCP adapters only with the
features that use them. They depend inward on Core contracts; driving adapters may
also reference Analysis to invoke solution-wide services. Analysis must not reference
CLI, MCP, reporting or the compiler adapter. Core must not depend on any adapter.

The dependency tests evaluate MSBuild items, including imported references, in Debug
and Release. They enforce the three-project graph and Core's lack of external
dependencies. New production projects require an explicit entry in those checks and
an update to this decision. This establishes a checkable boundary before rules exist.

## Decision: ports describe domain data, adapters handle technology

The following are ownership decisions, not interfaces implemented by F01. F02 defines
the shared rule, diagnostic and result contracts; subsequent features introduce
specific ports when their use cases exist.

| Port or contract | Owner and direction | Adapter responsibility |
| --- | --- | --- |
| Analysis request/result | Core input port and neutral result contract | Analysis implements the solution use case; CLI and MCP invoke it |
| Repository/module/dependency snapshot | Core input data and source-provider port | Analysis loads a workspace and maps host symbols and evidence into neutral data |
| Policy loading | Core policy contract and outbound loading port | Configuration integration reads YAML and validates it before evaluation |
| Rule evaluation | Core input/output contracts and pure evaluation | Engines supply only the scope and evidence they can establish |
| Diagnostics and evidence | Core output contract | Console, JSON/SARIF and MCP adapters render results without changing rule semantics |

File paths and source spans may be represented as neutral values. Roslyn `Solution`,
`Compilation`, `ISymbol` and syntax nodes must remain on the adapter side of a port.
Core interfaces must not expose SDK, serializer or transport types. Severity policy,
quality-gate decisions and presentation remain separate from rule evaluation.
Cancellation and failed/incomplete analysis must be expressible without a CLI exit
code in Core; CLI maps those outcomes to exit codes. An unavailable engine or an
incomplete snapshot must never be presented as a successful clean scan.

## Decision: separate solution-wide and compiler-hosted analysis

### Solution-wide engine

Analysis runs in a .NET 10 application hosted by CLI or MCP. It can load `.sln`,
`.slnx` and project inputs through MSBuildWorkspace (F04), normalize repository and
logical module data, and construct a dependency graph across projects. This is the
authoritative scope for module/layer dependencies and whole-module cycle detection
(AG001–AG003). Loading diagnostics, missing context and cancellation propagate to
the shared result rather than silently removing graph nodes.

Workspace loading belongs to this engine. It may execute repository MSBuild tasks;
F04 must define isolation and trust behavior before loading untrusted inputs.

### Compiler-hosted engine

A future compiler adapter (F12) runs inside Roslyn's compiler/IDE host. It receives
the current compilation and semantic context from that host, converts available
facts into Core data and maps evaluation results back to Roslyn diagnostics. It
must not launch the CLI, open an MSBuildWorkspace, perform an independent restore or
assume it can see every project in the solution.

Its initial scope is semantic checks for AG004–AG006 where the current compilation
has sufficient context. Whole-solution cycle detection remains with Analysis. The
two engines share rule identities and pure evaluation where their inputs support
the same semantics; missing context is explicit. F16 will verify parity on supported
cases and document generated-code, incomplete-compilation and host limitations.

All three initial projects target `net10.0`. This does **not** establish compiler-host
compatibility: a `net10.0` Core assembly cannot simply be packaged with a broadly
compatible analyzer. Before F12 ships, select and test the supported Roslyn/IDE host
matrix and provide a compatible target for the shared pure Core surface (for example,
multi-target Core for `netstandard2.0` and `net10.0`, or extract a compatible shared
library). Keep .NET 10 workspace orchestration outside that surface. No analyzer
project or host-compatibility claim is introduced by F01.

## Consequences

- The repository builds without Roslyn, MCP, YAML or reporting packages in production.
- The solution engine can evolve without coupling Core to workspace or console APIs.
- Compiler diagnostics cannot claim whole-solution coverage merely by reusing a rule ID.
- Extra adapters and concrete port APIs are deferred until required, avoiding unused
  implementations ahead of their feature contracts.
- CI enforces formatting, dependency direction and CLI behavior on all three desktop
  operating systems. Dependency lock files and the SDK policy make clean restores
  reproducible within the selected SDK patch band.
