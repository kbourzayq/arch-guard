# Architecture and analysis flows

Architecture Guardian separates architecture checks from the technology used to
load a repository, invoke analysis or display its results. Core owns neutral data
and pure evaluation contracts; Analysis coordinates a run; hosts own their inputs,
outputs and process behavior.

This document explains the structure and flows. [ADR 0001](decisions/0001-component-boundaries-and-analysis-engines.md)
records the decisions and constraints behind them.

## What exists today

| Area | Current implementation | Planned work |
| --- | --- | --- |
| Core contracts and rules | Rule catalog, findings, diagnostics, architecture/severity policy, project-policy resolution, requests, coverage outcomes, repository/module model, evidence graph and AG001–AG006 module/layer/cycle/implementation/reference evaluation | Extended rules |
| Analysis | `AnalysisService<TContext>` over prepared contexts; trusted `.sln`/`.slnx`/`.csproj` loading through `MSBuildWorkspaceLoader`; repository normalization and dependency extraction | Future host integrations |
| CLI | System.CommandLine help/version, `scan`, `inspect`, `explain` and trusted `load`; explicit coverage and gate exit codes | Tool packaging |
| Configuration | Strict v1 YAML loading through `IPolicyLoader`, structural and reference validation | Additional policy features |
| Compiler analyzer | netstandard2.0 Roslyn adapter, AG001/AG002/AG004/AG005/AG006 semantic uses, AdditionalFiles snapshot, labeled cross-engine parity | Extended rules |
| MCP | Official C# SDK stdio host, operator-owned root/trust, bounded requests, shared application services; empty tool catalog | Architecture and analysis tools |
| Other adapters | None | Reports |
| Quality gates | Inclusive severity threshold; noncompleted coverage always fails | Baselines |

F01–F17 establish the current implementation. The
[roadmap](https://github.com/kbourzayq/arch-guard/issues/44) tracks the remaining
features. Diagrams marked **planned** show intended integration, not commands or
adapters that can already be used.

## Project dependencies and responsibilities

Arrows in this diagram mean direct production project references, not runtime calls:

```mermaid
flowchart LR
    Cli["ArchitectureGuardian.Cli<br/>Arguments and composition<br/>Console and exit codes"]
    Analysis["ArchitectureGuardian.Analysis<br/>Orchestration, workspace loading, normalization and extraction"]
    Core["ArchitectureGuardian.Core<br/>Neutral contracts and metadata<br/>Pure policy"]
    Configuration["ArchitectureGuardian.Configuration<br/>YAML parsing and validation"]

    Mcp["ArchitectureGuardian.Mcp<br/>Stdio protocol and read-only application composition"]
    Mcp --> Analysis
    Mcp --> Configuration
    Cli --> Analysis
    Cli --> Configuration
    Analysis --> Core
    Configuration --> Core
    Analyzers["ArchitectureGuardian.Analyzers<br/>Host compilation and AdditionalFiles<br/>Source-links pure Core semantics; no project references"]
```

Core uses only the .NET base library. It performs no file, network or process access
and exposes no Roslyn, MSBuild, serializer or transport objects. Analysis converts
technology-specific inputs into neutral facts before invoking rules. CLI can consume
Core contracts through its transitive reference to Core.
Configuration depends only on Core and keeps YamlDotNet outside the domain. CLI
references Configuration and Analysis to compose policy-driven commands; its
System.CommandLine package stays outside the shared services. MCP references the same
Analysis and Configuration adapters and owns its SDK/hosting packages. Its current
empty tool catalog and transport contracts are documented in the [MCP guide](../mcp.md).

The service interface and its implementation live on opposite sides of this boundary:

| Responsibility | Owner | Entry point |
| --- | --- | --- |
| Describe the analysis use case | Core | [`IAnalysisService<TContext>`](../../src/ArchitectureGuardian.Core/Analysis/IAnalysisService.cs) |
| Coordinate registered rules and severity policy | Analysis | [`AnalysisService<TContext>`](../../src/ArchitectureGuardian.Analysis/Orchestration/AnalysisService.cs) |
| Load trusted workspace context and report loading coverage | Analysis | [`MSBuildWorkspaceLoader`](../../src/ArchitectureGuardian.Analysis/Workspaces/MSBuildWorkspaceLoader.cs), [`WorkspaceLoadResult`](../../src/ArchitectureGuardian.Analysis/Workspaces/WorkspaceLoadResult.cs) |
| Normalize workspace declarations and logical ownership | Analysis / Core | [`RepositoryModelBuilder`](../../src/ArchitectureGuardian.Analysis/Repositories/RepositoryModelBuilder.cs), [`RepositoryModel`](../../src/ArchitectureGuardian.Core/Repositories/RepositoryModel.cs) |
| Extract dependencies and retain origin evidence | Analysis / Core | [`DependencyGraphBuilder`](../../src/ArchitectureGuardian.Analysis/Dependencies/DependencyGraphBuilder.cs), [`DependencyGraph`](../../src/ArchitectureGuardian.Core/Dependencies/DependencyGraph.cs) |
| Define a check over neutral input | Core | [`IArchitectureRule<TContext>`](../../src/ArchitectureGuardian.Core/Rules/IArchitectureRule.cs) |
| Evaluate module dependencies and exact contract permissions | Core | [`ModuleDependenciesRule`](../../src/ArchitectureGuardian.Core/Rules/ModuleDependenciesRule.cs) |
| Evaluate configured layer dependencies | Core | [`LayerDependenciesRule`](../../src/ArchitectureGuardian.Core/Rules/LayerDependenciesRule.cs) |
| Evaluate domain technology restrictions and scoped exceptions | Core | [`DomainInfrastructureRule`](../../src/ArchitectureGuardian.Core/Rules/DomainInfrastructureRule.cs) |
| Detect module cycles with reproducible paths and evidence | Core | [`ModuleCyclesRule`](../../src/ArchitectureGuardian.Core/Rules/ModuleCyclesRule.cs) |
| Identify built-in checks and their help URLs | Core | [`RuleCatalog`](../../src/ArchitectureGuardian.Core/Rules/RuleCatalog.cs) |
| Load YAML through a neutral port | Core / Configuration | [`IPolicyLoader`](../../src/ArchitectureGuardian.Core/Policies/IPolicyLoader.cs), [`YamlPolicyLoader`](../../src/ArchitectureGuardian.Configuration/Yaml/YamlPolicyLoader.cs) |
| Resolve project ownership and dependency permissions | Core | [`ResolvedPolicy`](../../src/ArchitectureGuardian.Core/Policies/ResolvedPolicy.cs) |
| Check completed coverage and severity threshold | Core | [`GatePolicy`](../../src/ArchitectureGuardian.Core/Policies/GatePolicy.cs) |
| Carry findings, reporting levels and coverage | Core | [`RuleViolation`](../../src/ArchitectureGuardian.Core/Rules/RuleViolation.cs), [`Diagnostic`](../../src/ArchitectureGuardian.Core/Diagnostics/Diagnostic.cs), [`AnalysisResult`](../../src/ArchitectureGuardian.Core/Analysis/AnalysisResult.cs) |

The generic context is supplied by the caller and must stay stable throughout the
run. AG001–AG006 consume `DependencyBuildResult`, including F05's `RepositoryModel`,
F06's graph and extraction coverage; the service also supports other neutral scopes.
Concrete rule implementations remain separate
from catalog metadata.

## Source organization

Folders group types by scope and responsibility. Each file's namespace is the
project root namespace followed by its relative folder path; for example,
`Policies/ArchitecturePolicy.cs` in the Core project uses
`ArchitectureGuardian.Core.Policies`. Groups of types use plural names such as
`Policies`, `Rules` and `Diagnostics`. Each top-level type has its own matching file;
nested implementation helpers stay with their owner.

| Project | Folder / namespace suffix | Responsibility |
| --- | --- | --- |
| Core | `Analysis` | Requests, results, coverage outcomes and the analysis service port |
| Core | `Diagnostics` | Reported findings, evidence, affected targets, severity levels and source coordinates |
| Core | `Rules` | Evaluation contracts, rule descriptors, catalog, AG001–AG006 dependency/cycle rules and severity-free violations |
| Core | `Policies` | Architecture declarations, loading port, matching, resolution, permissions, severity and gate policy |
| Core | `Repositories` | Immutable repository, project, assembly, namespace, type, module, contract and layer data and build coverage |
| Core | `Dependencies` | Immutable evidence graphs, aggregation, traversal, cyclic components and extraction coverage |
| Core | `Canonicalization` | Internal deterministic encoding, hashing and collection ordering |
| Analysis | `Orchestration` | Reusable analysis service implementation |
| Analysis | `Repositories` | Workspace-to-Core normalization and policy ownership composition |
| Analysis | `Dependencies` | Evaluated reference and semantic dependency extraction with source evidence |
| Analysis | `Workspaces` | Trusted workspace loading, SDK registration, build-context and coverage validation |
| Configuration | `Yaml` | YAML parsing and loading |
| Configuration | `Validation` | Configuration error contracts with source positions |
| Analyzers | `Policies` / project root | Compiler snapshot validation / compilation-local semantic diagnostics |
| MCP | `Hosting`, `Requests` | Stdio host, operator configuration, physical root boundary, request limits and shared-service adaptation |
| CLI | Project root | Namespaced process entry point, arguments, console output and exit codes |

Unit tests use `Analysis`, `Dependencies`, `Diagnostics`, `Policies`, `Repositories`, `Rules` and `Workspaces` folders and
matching namespaces. Workspace fixtures live under `tests/Fixtures/Workspaces` and
are copied to temporary directories for integration tests. Architecture tests group dependency checks under `Dependencies`,
process smoke tests under `Cli`, and repository/process helpers under `Infrastructure`.
This organization does not change the project dependencies or component ownership.

## Following a request through the current service

A caller prepares an `AnalysisRequest<TContext>` and supplies rule implementations
when constructing the service. The service evaluates only those registered rules,
applies severity policy and returns an `AnalysisResult`. Its operational issues
remain separate from architecture violations.

The [service sequence](../shared-contracts.md#service-execution-flow) shows the calls,
and the [outcome flow](../shared-contracts.md#outcome-flow) shows how completion,
missing coverage, failure and cancellation affect the result. CLI `load` validates
workspace context separately. AG001–AG006 can be composed through the reusable API;
CLI `scan` composes that API with policy loading, extraction and severity gates.

The [catalog and documentation flow](../rule-catalog.md#how-catalog-metadata-reaches-a-diagnostic)
explains how metadata reaches a diagnostic. Catalog access is entirely local;
opening a documentation URL is an optional browser action after reporting.

## Solution-wide CLI flow

F10 assembles the CLI flow below. F02 supplies reusable orchestration
and contracts; F03 supplies YAML loading, policy resolution, rule selection and
severity gates. F04 supplies trusted workspace loading and F05 supplies neutral
repository normalization and ownership composition. F06 supplies dependency
extraction, evidence aggregation and deterministic traversal/cycles. F07 supplies
AG001 module-dependency evaluation; F08 supplies AG002 layer-dependency evaluation.
F09 supplies AG003 module-cycle evaluation. The MCP foundation registers these
same services; future MCP tools and report adapters reuse them.

```mermaid
flowchart TD
    Host["CLI — F10<br/>scan command"]
    Configuration["Configuration adapter — implemented<br/>Read and validate YAML policy — F03"]
    Workspace["Analysis workspace integration — implemented<br/>Load solution or projects — F04"]
    Context["Neutral repository model — implemented<br/>Normalize declarations and ownership — F05"]
    Graph["Dependency graph — implemented<br/>Evidence, aggregation and traversal — F06"]
    Service["AnalysisService<br/>Run registered rules and apply severity policy"]
    Rules["Core rule implementations<br/>AG001–AG006 — implemented F07–F09, F13–F15"]
    Result["AnalysisResult<br/>Coverage, diagnostics and operational issues"]
    Output["CLI console output — F10<br/>MCP stdio — F17; tools and reports — planned"]
    Gate["GatePolicy — implemented<br/>Coverage and severity threshold<br/>Baselines — planned"]

    Host --> Configuration
    Host --> Workspace
    Workspace --> Context
    Configuration --> Context
    Context --> Graph
    Graph --> Service
    Configuration -->|"Validated neutral inputs and severity policy"| Service
    Service -->|"Evaluate using prepared context"| Rules
    Rules -->|"Findings, issues and coverage"| Service
    Service --> Result
    Result --> Output
    Result --> Gate
```

Configuration supplies validated `ArchitecturePolicy`. `ResolvedPolicy` maps a
complete, caller-supplied inventory to modules, layers and public contracts before
rules run. `RepositoryModelBuilder` composes workspace normalization with this
resolution and stores both the policy and mappings in `RepositoryModel`. Selection
rejects unavailable configured rules. The neutral context carries these inputs;
the request carries severity settings. Rule logic determines violations;
severity policy selects reporting levels; quality gates determine whether an
evaluated result is acceptable. A completed run can contain error diagnostics.
Incomplete coverage must never become a successful clean scan.

Workspace loading requires explicit trust because design-time builds can execute
repository MSBuild tasks. See [workspace loading](../workspace-loading.md) for isolation,
framework selection and restore behavior. Loading failures and missing projects remain
coverage problems rather than disappearing from the graph.

## Compiler-hosted flow

The compiler adapter has a different input source and lifecycle. It receives the
current compilation from its host and maps only the facts available in that scope:

```mermaid
flowchart TD
    Host["Compiler or IDE host<br/>Current Roslyn compilation"]
    Adapter["Compiler adapter — F12<br/>Map available facts to neutral inputs"]
    Rules["Source-linked Core dependency semantics<br/>AG001/AG002/AG004/AG005/AG006"]
    Diagnostics["Compiler adapter<br/>Map findings to Roslyn diagnostics"]

    Host --> Adapter
    Adapter --> Rules
    Rules --> Diagnostics
```

This adapter uses the host's compilation rather than opening an MSBuildWorkspace,
restoring projects or launching CLI. One compilation cannot establish whole-solution
module cycles. The solution engine owns that scope; sharing a rule ID does not grant
the compiler adapter additional coverage.

The compiler adapter targets netstandard2.0 with a Roslyn 4.8 API floor and has no
project references. It source-links the small pure Core `DependencySemantics`,
`ForbiddenReferenceSemantics` and `DomainInfrastructureSemantics` sources for shared
AG001/AG002/AG004/AG005/AG006 metadata,
permissions and explicit reference matching. The [compiler guide](../compiler-analyzer.md)
documents policy distribution, generated code, scope and host validation. The other
projects remain net10.0. F16 validates the shared semantic surface and explicit coverage
differences in the [semantic accuracy matrix](../semantic-accuracy.md).

## Where to read next

- [Repository model](../repository-model.md): normalization, portable identities,
  multi-project modules, contracts, layer membership and build coverage.
- [Dependency graph](../dependency-graph.md): extraction, origin evidence, logical
  aggregation, deterministic traversal/cycles and semantic coverage.
- [Shared contracts](../shared-contracts.md): service composition, execution, coverage,
  cancellation, locations and deterministic identities.
- [Rule catalog](../rule-catalog.md): metadata, documentation links and the canonical
  AG001–AG006 meanings.
- [Policy guide](../policy.md): v1 schema, matching and ownership, dependencies,
  rule selection, severity gates and host composition.
- [ADR 0001](decisions/0001-component-boundaries-and-analysis-engines.md): dependency
  boundaries, ownership decisions and the two-engine constraints.
- [README](../../README.md): build checks and commands available today.
