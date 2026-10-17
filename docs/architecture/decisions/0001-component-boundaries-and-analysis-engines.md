# ADR 0001: Component boundaries and two analysis engines

- Status: Accepted
- Date: 2026-10-17
- Delivery: [F01](https://github.com/kbourzayq/arch-guard/issues/1),
  [F02](https://github.com/kbourzayq/arch-guard/issues/2),
  [F03](https://github.com/kbourzayq/arch-guard/issues/3)

For diagrams and an implementation-status guide, see the
[architecture overview](../overview.md). This record explains the decisions behind
those flows.

## Context

Architecture Guardian must evaluate architecture policies from a complete solution
and eventually provide compiler-hosted semantic diagnostics. CLI, MCP and reporting
protocols must not determine the policy model or rule semantics. A compiler invocation
does not have the same repository view or lifecycle as a standalone scan.

## Decision: dependencies point toward Core

F01 started with three .NET 10 projects. F03 adds Configuration as an inward-facing
adapter. The current production references are:

```text
ArchitectureGuardian.Cli --> ArchitectureGuardian.Analysis --> ArchitectureGuardian.Core
     console/composition        solution application services       pure policy/evaluation
ArchitectureGuardian.Configuration -------------------------> ArchitectureGuardian.Core
     YAML loading/validation
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

Configuration now implements Core's `IPolicyLoader` port and references only Core.
YamlDotNet 16.3.0 is confined to this adapter: the base library has no YAML parser,
and a mature parser preserves source marks and detects malformed documents without
introducing serializer types into Core. Strict schema checks run before constructing
neutral policy. No workspace or rule-discovery dependency is introduced.

Add reporting, compiler-analyzer and MCP adapters only with the features that use
them. They depend inward on Core contracts; driving adapters may
also reference Analysis to invoke solution-wide services. Analysis must not reference
CLI, MCP, reporting or the compiler adapter. Core must not depend on any adapter.

The dependency tests evaluate MSBuild items, including imported references, in Debug
and Release. They enforce the four-project graph and Core's lack of external
dependencies. New production projects require an explicit entry in those checks and
an update to this decision. This establishes a checkable boundary before rules exist.

## Decision: ports describe domain data, adapters handle technology

F01 established the following ownership decisions. F02 implements the shared rule,
diagnostic, severity policy, request and result contracts in Core, with host-independent
orchestration in Analysis. F03 implements the neutral architecture-policy contracts,
pure resolution and loading port. Subsequent features introduce remaining ports
when their use cases exist.

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

### F02 contract and service decisions

`IArchitectureRule<TContext>` evaluates a prepared neutral context and returns
`RuleViolation` values without severity. `SeverityPolicy` maps those findings to
`Diagnostic` severities; neither layer decides quality-gate outcomes. `RuleCatalog`
fixes the AG001–AG006 identities without introducing rule implementations.

`IAnalysisService<TContext>` is the reusable Core input port implemented by
`AnalysisService<TContext>` in Analysis. Generic contexts let later features supply
their normalized repository model without defining a placeholder graph or exposing
workspace objects now. Hosts must keep the context stable during evaluation.

The service evaluates registered rules in ordinal ID order, applies severity policy
and preserves partial findings and operational issues. Results explicitly distinguish
completed, incomplete, failed and cancelled work. No rules is incomplete, rule
exceptions fail the run, and requested cancellation returns a cancelled result.
Incomplete/failed results require explanatory issues; completed results cannot carry
operational issues. Diagnostics with error severity can still belong to completed
analysis, because completion describes coverage rather than policy compliance.

Contracts snapshot their collections. Canonical content produces reproducible
ordering and SHA-256 diagnostic identities independently of severity. F22 will define
baseline fingerprints and movement/rename matching; F02 does not promise identity
stability across changed source, wording or contract versions. See the
[shared contracts guide](../../shared-contracts.md) for the complete behavior.

### F03 policy and configuration decisions

`ArchitecturePolicy` snapshots module and layer declarations, whole-project public
contracts, selected canonical rule IDs, reporting severities and a severity gate.
`YamlPolicyLoader` implements the Core loading port in Configuration. It accepts
an explicit file path, validates the strict v1 document, and preserves read,
configuration and cancellation failures. The published JSON Schema supports editor
validation; Core validates unique identities and declared dependency references.

`ResolvedPolicy` takes a complete project inventory from the caller and performs
no I/O. Identities are project paths relative to the policy directory. Globs use
ordinal case-sensitive matching across operating systems. Every project must have
exactly one module and layer; contracts must be disjoint subsets of their module.
Unknown references, unmatched projects, unused selectors and ownership overlaps fail
instead of weakening coverage. See the [policy guide](../../policy.md) for the exact grammar.

Dependency permission checks consume discovered project edges without discovering
violations themselves. Module and contract references are explicitly distinguished;
permissions are direct and directional. Rule selection rejects missing selected
implementations. Severity remains a reporting decision after discovery. `GatePolicy`
checks an inclusive severity threshold and always rejects incomplete, failed or
cancelled coverage. Baseline-aware gates remain F22. CLI composition and scanning
remain F10; adding Configuration does not introduce a working scan command.

## Decision: separate solution-wide and compiler-hosted analysis

### Solution-wide engine

The solution-wide engine is designed to run in a .NET 10 application hosted by CLI
or MCP. Loading `.sln`, `.slnx` and project inputs through MSBuildWorkspace is planned
in F04; repository normalization and dependency graph construction follow in F05–F06.
This is the authoritative scope for module/layer dependencies and whole-module cycle
detection (AG001–AG003). Loading diagnostics, missing context and cancellation must
propagate to the shared result rather than silently removing graph nodes. F02
implements orchestration over prepared contexts; workspace integration is still pending.

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

- The repository builds without Roslyn, MCP or reporting packages in production;
  only Configuration depends on the YAML package.
- The solution engine can evolve without coupling Core to workspace or console APIs.
- Compiler diagnostics cannot claim whole-solution coverage merely by reusing a rule ID.
- Extra adapters and concrete port APIs are deferred until required, avoiding unused
  implementations ahead of their feature contracts.
- CI enforces formatting, dependency direction and CLI behavior on all three desktop
  operating systems. Dependency lock files and the SDK policy make clean restores
  reproducible within the selected SDK patch band.
