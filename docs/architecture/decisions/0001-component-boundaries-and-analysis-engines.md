# ADR 0001: Component boundaries and two analysis engines

- Status: Accepted
- Date: 2026-10-17
- Delivery: [F01](https://github.com/kbourzayq/arch-guard/issues/1),
  [F02](https://github.com/kbourzayq/arch-guard/issues/2),
  [F03](https://github.com/kbourzayq/arch-guard/issues/3),
  [F04](https://github.com/kbourzayq/arch-guard/issues/4),
  [F05](https://github.com/kbourzayq/arch-guard/issues/5),
  [F06](https://github.com/kbourzayq/arch-guard/issues/6),
  [F07](https://github.com/kbourzayq/arch-guard/issues/7),
  [F08](https://github.com/kbourzayq/arch-guard/issues/8),
  [F09](https://github.com/kbourzayq/arch-guard/issues/9),
  [F10](https://github.com/kbourzayq/arch-guard/issues/10)

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
ArchitectureGuardian.Cli --> ArchitectureGuardian.Configuration --> ArchitectureGuardian.Core
                                  YAML loading/validation
```

An arrow means a project reference. CLI can consume Core types through Analysis's
transitive reference. Core has no project, package or explicit assembly references;
it uses only the .NET base class library. Core must never reference Roslyn, MSBuild,
CLI, MCP, serializers, reporting adapters or their transport/host types. It does not
load files, discover SDKs, invoke processes or write reports.

Analysis is the solution-wide engine and application-service implementation reusable
by CLI and the MCP adapter. It references Core and owns workspace loading
and conversion of Roslyn/MSBuild data into neutral Core inputs. F04 adds Roslyn C#
workspaces, MSBuild integration and SDK discovery in Analysis. CLI composes the
services, handles arguments and maps results to console output and exit codes.
It contains no rule evaluation.

Configuration implements Core's `IPolicyLoader` port and references only Core.
YamlDotNet 18.1.0 is confined to this adapter: the base library has no YAML parser,
and a mature parser preserves source marks and detects malformed documents without
introducing serializer types into Core. Strict schema checks run before constructing
neutral policy. No workspace or rule-discovery dependency is introduced.

Add reporting, compiler-analyzer and MCP adapters only with the features that use
them. They depend inward on Core contracts; driving adapters may
also reference Analysis to invoke solution-wide services. Analysis must not reference
CLI, MCP, reporting or the compiler adapter. Core must not depend on any adapter.

The dependency tests evaluate MSBuild items, including imported references, in Debug
and Release. They enforce the production-project graph and Core's lack of external
dependencies. New production projects require an explicit entry in those checks and
an update to this decision. This establishes a checkable boundary before rules exist.

Package versions are authored centrally in the root `Directory.Packages.props`,
with dependency ownership and asset/privacy metadata retained in project files.
Conditional entries preserve the analyzer tests' Roslyn 4.8 baseline independently
of solution-workspace versions. Central declarations do not add dependencies to
Core; there are no global package references or central transitive pins. Fixture
roots opt out through their own `Directory.Packages.props` so standalone and copied
restores, including the independently locked generator, remain isolated.

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
ordering and SHA-256 diagnostic identities independently of severity. F22 defines separate
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
cancelled coverage. F22 adds baseline-aware gates separately from evaluation.
F10 composes these contracts through CLI scanning; Configuration itself remains independent of workspace loading
and hosts.

## Decision: separate solution-wide and compiler-hosted analysis

### Solution-wide engine

The solution-wide engine is designed to run in a .NET 10 application hosted by CLI
or MCP. F04 loads `.sln`, `.slnx` and C# project inputs through MSBuildWorkspace;
F05 normalizes the loaded context into neutral repository data. F06 constructs the
dependency evidence graph from evaluated references and semantic uses.
This is the authoritative scope for module/layer dependencies and whole-module cycle
detection (AG001–AG003). Loading diagnostics, missing context and cancellation must
propagate to the shared result rather than silently removing graph nodes. F02
implements orchestration over prepared contexts; F04 provides workspace context separately.

Workspace loading belongs to this engine. It requires explicit trust before SDK
discovery or design-time build execution. No automatic restore runs. Workspace
warnings, compiler/generator errors, generator failures, missing restore assets and loading exceptions yield
incomplete coverage; cancellation stays distinct. CLI `load` maps unfinished coverage
to exit 2 without claiming a rule scan. Framework selection is explicit for
multi-targeted projects, and coverage applies to one configuration/framework per run.
See the [workspace loading guide](../../workspace-loading.md) for isolation and SDK
lifecycle constraints.

Roslyn `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Workspaces.MSBuild` 5.9.0
provide C# semantic context and solution loading. `Microsoft.Build.Locator` 1.11.2
selects an installed SDK with the input's `global.json`. The MSBuild 18.9.6 compile-time
references (`Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.NET.StringTools`)
exclude runtime assets and remain private so SDK-provided assemblies load only after
registration. No new project or dependency toward Core is introduced; workspace
results expose Roslyn context only inside the Analysis adapter. F05 adds a separate
normalization API that converts that context into Core data.
Reference validation retains the workspace's effective global properties, including
solution context, and matches Roslyn's design-time defaults. Generator errors require
an additional pass through Roslyn's public generator driver because the pinned
workspace API does not expose generator diagnostics publicly; original source trees
and generator inputs are preserved for that pass.

### F05 normalization and logical ownership

Core's `RepositoryModel` contains the input identity, projects, evaluated output
assemblies, source namespaces/types, logical modules, public contracts and layers.
Analysis's `RepositoryModelBuilder` converts the workspace's current compilations
without retaining host objects. It includes explicit generated declarations and
collapses partial types; referenced metadata and implicit compiler helper types are
excluded. No new package, project or dependency boundary is introduced.

The caller supplies the policy directory. Input/project identities are canonical
relative paths; declaration identities hash project-scoped names, with a relative
source-file discriminator for authored file-local types. Absolute checkout paths, workspace
IDs and output paths are excluded. Generated file-local types use logical generator
assembly/type/hint identities instead of physical declaring paths, preserving their
IDs across configurations and external intermediate directories. Namespace names use
raw symbol components rather than C# keyword escaping. Core snapshots and orders
collections and reuses
`ResolvedPolicy` for strict module/layer/contract membership. A module can span
multiple layers; contract access remains a separate direct permission decision.

`RepositoryBuildResult` retains loading issues and distinguishes completed,
incomplete, failed and cancelled normalization. Invalid mappings never become clean
models. The model survives workspace disposal; hosts must inspect coverage before
evaluation. See the [repository model guide](../../repository-model.md) for the
identity fields and executable modular-monolith reference fixture. F07 consumes
this model and F06's evidence for module evaluation; F10 composes it in CLI scanning.

### F06 dependency extraction and evidence

Analysis's `DependencyGraphBuilder` normalizes the same workspace through
`RepositoryModelBuilder`, binds evaluated project/assembly references and semantic
type uses, and preserves source symbols, project pairs and portable source spans.
Core's `DependencyGraph` snapshots this evidence, merges direct edges and aggregates
them across policy module/layer boundaries without losing contract-sensitive project
origins. External metadata remains visible without receiving repository ownership.
Normalization and extraction share declaration identity construction, including
generated and authored file-local types.

Core owns iterative deterministic traversal and cyclic strongly connected component
discovery. Graph construction and traversal perform no I/O and expose no compiler,
serializer or presentation types. No packages or new component boundaries are added.
Compilation errors, unresolved/ambiguous context and dynamic operations prevent
completion; available evidence is retained. Static binding does not claim runtime
reflection or concrete dispatch targets. Rule evaluation and scanning composition
remain separate. See the [dependency graph guide](../../dependency-graph.md).

### F07 module-dependency evaluation

Core's `ModuleDependenciesRule` implements AG001 over the existing neutral
`DependencyBuildResult`. It checks original source/target project pairs through
`ResolvedPolicy.AllowsModuleDependency` and reports one evidence-backed finding per
forbidden pair. Facts are deduplicated across graph levels; approved public-contract
facts never authorize other projects in an aggregated module edge. Same-module
dependencies pass and external metadata remains outside module ownership checks.

The supplied repository and graph define scope. Out-of-inventory evidence prevents
completed coverage; extraction status/issues and partial findings are preserved.
Rule selection controls enabled state using v1's existing explicit rule IDs.
Severity and gates remain with the existing policies and service orchestration.
No package, component or host dependency is introduced. F08 and F09 add the
separate layer and cycle checks below; F10 exposes evaluation through CLI scanning.
See the [AG001 guide](../../rule-catalog.md#ag001-module-dependencies).

### F08 layer-dependency evaluation

Core's `LayerDependenciesRule` implements AG002 over the same neutral
`DependencyBuildResult`. It checks original project pairs through
`ResolvedPolicy.AllowsLayerDependency`, independently of module and contract
permissions. The policy's named layers and outgoing allow-lists define the matrix;
there is no fixed Clean Architecture layout. Same-layer dependencies pass.

Like AG001, AG002 merges repeated reference and semantic facts per forbidden pair,
retains source evidence and upstream coverage, and reports unmapped project evidence
as incomplete coverage. Rule selection, severity and gates reuse the existing
contracts. No schema, package, project or component dependency changes are required.
See the [AG002 guide](../../rule-catalog.md#ag002-layer-dependencies).

### F09 module-cycle evaluation

Core's `ModuleCyclesRule` implements AG003 over `DependencyBuildResult`, rebuilding
module edges from original project ownership. It excludes intra-module dependencies
and external metadata; allowed module/contract edges still participate. Iterative
strongly connected component discovery and breadth first path selection report one
reproducible, evidence-backed representative cycle per component without recursive
traversal or enumeration of every simple cycle.

Upstream coverage, issues and available findings are preserved. Unmapped project
evidence prevents completed coverage. Selection, severity and gates use the existing
contracts. No schema, package, project or component dependency changes are required.
See the [AG003 guide](../../rule-catalog.md#ag003-module-cycles).

### F10 CLI composition

CLI references Configuration directly to load explicit YAML policies, then composes
Analysis's workspace loader, repository/dependency builders and generic analysis
service. Core and Analysis keep their existing dependencies. System.CommandLine
2.0.13 belongs only to CLI: it supplies command parsing, generated help and async
invocation cancellation, replacing the hand-written argument parser.

`scan` selects available implementations before workspace execution, evaluates the
neutral context and applies `GatePolicy` after completed coverage. Exit codes are
0 for completed/pass, 1 for completed/gate failure and 2 for invalid, incomplete,
failed or cancelled work. Partial findings remain visible independently of coverage.
`inspect` normalizes ownership and reports policy boundaries without evaluating
rules. `explain` resolves canonical catalog metadata locally. `load` remains available
with the same trust and coverage contract. Console rendering stays inside CLI;
Configuration is not referenced from Analysis or Core.

The dependency boundary tests and repository policy explicitly permit CLI's
Configuration reference. The layer policy also permits semantic dependencies on Core,
matching the existing decision that CLI consumes neutral contracts transitively.
Direct project references remain Analysis and Configuration; layer checks include
semantic type uses as well as explicit project references. Lock files capture the
new parser and existing YAML adapter dependencies. No project or new component boundary is introduced.
See the [CLI guide](../../cli.md).

### Compiler-hosted engine

The F12 compiler adapter runs inside Roslyn's compiler/IDE host. It receives
the current compilation and semantic context from that host, converts available
facts into neutral ownership identities and maps shared permission decisions to Roslyn diagnostics. It
must not launch the CLI, open an MSBuildWorkspace, perform an independent restore or
assume it can see every project in the solution.

Its initial scope is semantic uses for AG001/AG002 with supplied assembly ownership;
F13 adds AG004; F14 adds AG005 explicit assembly/namespace restrictions with source-linked
portable matching and bound identities. The analyzer source-links only
`DependencySemantics.cs`, `ForbiddenReferenceSemantics.cs` and
`DomainInfrastructureSemantics.cs`; boundary tests enforce that exact portable surface.
F15 adds AG006 configuration-driven technology checks and layer-scoped namespace exceptions. Whole-solution cycle detection remains with Analysis. The
two engines share rule identities and pure evaluation where their inputs support
the same semantics; missing context is explicit. F16 verifies parity on supported
cases and documents generated-code, incomplete-compilation and host limitations in
the [semantic accuracy guide](../../semantic-accuracy.md).

All three initial projects target `net10.0`. This does **not** establish compiler-host
compatibility: a `net10.0` Core assembly cannot simply be packaged with a broadly
compatible analyzer. F12 source-links the small portable permission surface into its netstandard2.0
adapter and validates its Roslyn API floor and packaged SDK consumers. Keep .NET 10
workspace orchestration outside that surface; see the F12 decision below.

## F24 reporting adapter

`ArchitectureGuardian.Reporting` references only Core. It maps `AnalysisResult`
and optional `BaselineComparison` to versioned JSON and SARIF 2.1.0, preserving
rule metadata, effective severities, evidence, semantic fingerprints, coverage and
baseline classification. The host supplies the separately evaluated gate outcome.
Serialization and report-file I/O use only the .NET base library; no new runtime
packages are required. CLI references Reporting directly. Core, Analysis,
Configuration, MCP and Analyzers gain no references to it.

The adapter publishes files through a sibling temporary file and atomic replacement;
write failures map to CLI operational exit `2`. Schemas and mappings are documented
in [reports](../../reports.md); CI validates real exports against JSON v1 and the
vendored OASIS SARIF schema on each OS. Dependency-boundary tests enforce the new
project's solution membership and Core-only direction in Debug and Release.

## Consequences

- Analysis owns workspace/MSBuild packages, Analyzers owns compiler-only Roslyn
  packages and Configuration owns the YAML package;
  Core remains independent of external packages. MCP transport is implemented in F17;
  MCP architecture tools are implemented in F18; F19 adds solution analysis; F23 implements full-context Git comparison.
  F24 adds the Reporting adapter for JSON v1 and SARIF 2.1.0 exports.
- The solution engine can evolve without coupling Core to workspace or console APIs.
- Compiler diagnostics cannot claim whole-solution coverage merely by reusing a rule ID.
- Extra adapters and concrete port APIs are deferred until required, avoiding unused
  implementations ahead of their feature contracts.
- CI enforces formatting, dependency direction and CLI behavior on all three desktop
  operating systems. Dependency lock files and the SDK policy make clean restores
  reproducible within the selected SDK patch band.

## F12: portable compiler adapter

`ArchitectureGuardian.Analyzers` targets netstandard2.0 and Roslyn 4.8.0. It has no
project references and source-links only Core's internal `DependencySemantics.cs`:
base-library-only AG001/AG002 metadata and direct permission decisions. Core compiles
the same source, so its resolved policy and the compiler adapter share these semantics.
This deliberately small source boundary avoids loading net10.0 Core, Analysis,
Configuration, YAML or Workspaces dependencies in compiler hosts. Boundary tests
require this explicit fifth production project; the analyzer release tracking files
and locked packages keep compiler dependencies visible.

The adapter validates an assembly ownership snapshot supplied through AdditionalFiles,
then resolves bound type/member uses from its host compilation. It emits canonical
AG001/AG002 meanings with per-use spans and leaves severity to Roslyn configuration.
It never claims full repository coverage or evaluates cycles. Missing configuration
produces operational AG000 errors. The solution engine keeps its existing grouped
project-pair diagnostics, policy loading, coverage statuses and gates.

The [compiler guide](../../compiler-analyzer.md) specifies snapshot distribution,
source-linked semantics, generated-code behavior, cancellation, concurrent isolation,
the supported compiler API baseline and package validation matrix. Interactive IDE
UI testing remains separate from driver/workspace compatibility validation.

## F13: semantic implementation boundaries

Core's `ImplementationBoundariesRule` consumes F06's semantic target symbols and
original project ownership. It rejects cross-module uses of non-contract projects,
including public implementation types, without treating unused project references as
semantic access. Findings group by project pair and target symbol with deduplicated
source evidence. Same-module and declared whole-project contract uses pass AG004;
AG001 permissions and AG002 layers remain independent.

The compiler adapter reuses the portable `DependencySemantics` ownership predicate
and canonical AG004 metadata through the existing source link. It checks bound names
in its host compilation using the existing immutable snapshot; it does not load
workspaces or claim solution coverage. Generated code is included in both engines.
CLI registers AG004 for explicit policy selection. No schema, package, project or
component boundary changes are required. See the [AG004 guide](../../rule-catalog.md#ag004-implementation-boundaries)
and [compiler reference scope](../../compiler-analyzer.md#reporting-and-scope).

### F15 domain infrastructure references

Core's `DomainInfrastructureRule` combines resolved source layer membership with
bound assembly/namespace evidence. `LayerPolicy.DomainInfrastructure` carries optional
technology restrictions and target namespace exceptions; no layer or framework name
is special. The YAML adapter and v1 editor schema accept this additive object. CLI
registers AG006 and displays the restrictions during inspection. Existing policies
retain their behavior because the object and its lists default to no restrictions.

The compiler snapshot carries equivalent layer attributes and source-links only
`DomainInfrastructureSemantics.cs` for shared matching/metadata. It keeps the existing
explicit-name semantic surface and host severity settings. AG006 namespace exceptions
cannot override AG002's layer matrix or AG005's general restrictions; each rule keeps
its own findings and evidence. No project, package or component dependency is added.

## F17: local read-only MCP adapter

`ArchitectureGuardian.Mcp` is the sixth production project and targets net10.0.
It references Configuration for the injected policy loader and consumes Core
transitively. Analysis is added only when an actual workspace/analysis tool needs
it. ModelContextProtocol 2.3.0 owns stdio framing, protocol handling, attributed tool
discovery, dependency injection, schemas, invocation and cancellation. Standard
.NET hosting sends console logs to stderr; standard configuration and options
bind and validate workspace settings. Microsoft.Extensions.Hosting is pinned to
10.0.13. No HTTP hosting dependency is introduced.

F18 adds the first production tools below. `[McpServerTool]` methods
receive the services they need through normal DI and forward the SDK cancellation
token. The host does not add a custom stream, executor, application session or
eager analysis composition. Operator root/trust settings use host configuration;
operation-specific input validation and bounds belong with the tools.
Transport byte limits, application concurrency limits and extra request deadlines
are not imposed by this host. The SDK's initialization timeout is 10 seconds.

Solution membership, evaluated boundary tests, repository layer policy and lock
files track this adapter. SDK integration tests use an attributed test-only tool to
verify DI, shared policy loading, errors and cancellation; production discovery
only scans the executable assembly. See the [MCP guide](../../mcp.md).


## F18: bounded architecture and rule context

MCP now references Analysis alongside Configuration. `get_architecture` composes the
same `MSBuildWorkspaceLoader` and `RepositoryModelBuilder` used by CLI inspection,
so strict ownership and incomplete coverage retain their existing meanings.
`list_rules` reads policy without executing builds, explicitly labeling its project
inventory as not loaded. `SolutionRules.All` is the single solution-engine inventory
used for CLI rule selection and MCP availability metadata; canonical descriptions
and documentation links remain in Core's catalog.

Tool-specific records and serialization stay inside MCP. The SDK generates input
and output schemas; results carry matching structured/text JSON with an explicit
schema version. Filters apply after complete inventory validation, retain all coverage
issues and preserve outgoing constraints. Payloads exceeding the documented bound
fail explicitly rather than returning silently truncated context. Explicit tool input
paths are checked under the configured root and reject descendant links; trusted
MSBuild remains outside any filesystem sandbox guarantee. The [MCP guide](../../mcp.md)
defines size limits, schemas, trust, cancellation and filtering semantics.

Dependency tests, the repository policy, lock files and architecture diagrams include
MCP's Analysis reference. No project, external package or Core dependency is added.

## F19: shared solution analysis and reserved graph comparison

`SolutionAnalysisService` owns the scan pipeline used by CLI `scan` and MCP
`analyze_solution`, including rule selection before workspace execution. Rules,
severity, deterministic diagnostic identities and coverage retain their existing
contracts. MCP adds bounded result projections with counts, evidence and an explicit
gate outcome; noncompleted analysis has no gate decision.

`analyze_changes` requires an explicit base reference and `solution` scope. F23
implements this entry point with full base/candidate snapshots; see the decision
below and the [change guide](../../changes.md).


## F20: deterministic violation explanations

Core's canonical evaluators retain neutral, immutable `ViolationContext` values
alongside each finding: ordered observed endpoints, violated policy constraints
and declared contract alternatives. Existing v1 identities and evidence remain
unchanged; projection metadata is excluded from that identity. Rule semantics and
provenance remain in Core; no new package, project or dependency boundary is added.

MCP `explain_violation` renders rule-level guidance using policy only, or resolves an
exact diagnostic ID through fresh `SolutionAnalysisService` evaluation. It has no
session cache or LLM dependency. Unknown/stale references fail after complete
analysis; incomplete coverage retains its issues and cannot establish staleness.
Remediation names configured contracts without inventing source locations or
replacement symbols, and warns that independent permissions still apply. Schemas,
limits, trust and cancellation follow the existing MCP adapter contract.

## F22: explicit diagnostic baselines

Core owns immutable semantic baseline identities, snapshots and conservative
one-to-one classification. Fingerprints version rule/target/context identity
separately from existing location-sensitive diagnostic IDs. Source movement can
retain identity; project/module/symbol renames and changed constraints require
explicit acceptance. Ambiguous matches remain new and cannot suppress a gate.
`GatePolicy.PassesNewFindings` checks completed coverage and only new severities;
the original full-result gate remains available to CLI without a baseline and MCP.

Analysis owns strict JSON persistence in `JsonBaselineStore`; serializers and file
access stay outside Core. CLI exposes `baseline update` and `scan --baseline`.
Updates require completed analysis and atomically replace an explicitly named file;
scans are read-only. Partial findings remain visible and operational failures
cannot become accepted debt or resolved-entry claims. No new package, production
project, YAML schema change or dependency boundary is introduced. See the
[baseline guide](../../baselines.md) for the v1 wire shape and matching fixtures.

## F23: full-context Git change analysis

Analysis owns Git capture, private temporary snapshot/index lifecycle and bounded
SDK processes. The base is the exact locally resolved commit; the candidate is
the on-disk tree including tracked and non-ignored untracked inputs. A private
Git index materializes base files without `git archive` export-attribute omissions
or mutation of the operator's index. Snapshot preparation uses cache-only locked
restore; neither Git history nor NuGet packages are downloaded. Both trees require
explicit operator trust. Configuration stays independent: hosts supply a policy
loader, applied independently to each tree (including MCP policy limits).

Both complete contexts run through `SolutionAnalysisService.AnalyzeAsync`. F22
fingerprints classify existing/new/resolved findings and the candidate policy gates
new findings only. Combined coverage blocks success if either tree is unavailable;
missing findings are never declared resolved from an incomplete candidate. Project
removal/rename is reflected in each tree's complete inventory and policy. CLI and
MCP render this result without implementing rule semantics. No project or package
dependency boundary changes are needed.
