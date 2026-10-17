# Dependency extraction and evidence graph

[F06](https://github.com/kbourzayq/arch-guard/issues/6) adds
`ArchitectureGuardian.Core.Dependencies` and
`ArchitectureGuardian.Analysis.Dependencies.DependencyGraphBuilder`. Core owns the
immutable graph, aggregation and traversal. Analysis owns compiler binding and
converts its observations into neutral evidence. No additional packages or component
dependencies are required.

## Build a graph

A host referencing Configuration and Analysis can consume the already restored,
trusted workspace:

```csharp
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;

var policy = await new YamlPolicyLoader().LoadAsync(policyPath, cancellationToken);
using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(
    new WorkspaceLoadRequest(inputPath, isTrusted: true), cancellationToken);
var result = await new DependencyGraphBuilder().BuildAsync(
    workspace, Path.GetDirectoryName(Path.GetFullPath(policyPath))!, policy,
    cancellationToken);

if (result.Status != AnalysisStatus.Completed || result.Graph is null)
{
    // Surface result.Status and result.Issues; retain any graph as partial evidence.
    return;
}

var moduleCycles = result.Graph.FindCycles(DependencyKind.Module, cancellationToken);
var moduleEdges = result.Graph.Edges.Where(edge => edge.Kind == DependencyKind.Module);
// Rule evaluation and rendering remain separate from extraction.
```

`DependencyBuildResult.Repository` contains the normalized declarations, policy and
resolved ownership used for this graph. `Graph`, `Status` and `Issues` describe
extraction and its coverage. The builder normalizes the same workspace through
`RepositoryModelBuilder`, performs no restore or workspace opening, and retains
loading and normalization issues. Both snapshots survive workspace disposal.
CLI `load` still checks workspace context only; scanning composition remains F10.

## Levels and evidence

| `DependencyKind` | Meaning |
| --- | --- |
| `Project` | An evaluated Roslyn project reference, including unused references |
| `Assembly` | A compilation assembly reference, including unused metadata references, or a semantic use |
| `Namespace` | A semantic use between namespaces scoped to their producing assemblies/projects |
| `Type` | A semantic use between explicit source types and referenced source or metadata types |
| `Module` | Direct dependencies crossing policy module boundaries |
| `Layer` | Direct dependencies crossing policy layer boundaries |

References use the selected workspace configuration and framework. Compilation
assembly identities map referenced symbols to source projects; ambiguous identities
produce an issue. External framework/package assemblies and referenced namespaces
and types have graph endpoints with null `ProjectPath`. They are not added to the
repository's source inventory or assigned policy ownership. Project edges describe
the compilation's project references, not arbitrary build-order-only MSBuild items.

Semantic extraction includes base types, interfaces, constraints, attributes,
generic arguments, nested types, arrays, pointers, function pointers, inferred
expression types, aliases at use sites, member/constructor calls, user-defined
conversions and operators, deconstruction assignments/foreach variables, positional
patterns, and foreach/await bindings. Nested deconstruction retains each
compiler-selected method. Intrinsic `nameof` expressions retain their argument's
dependencies without treating the intrinsic callee as an unresolved type. Explicit
generated source participates. Unused `using` directives and documentation comments do not create
dependencies. Constructed generic types use the normalized original declaration
identity and retain dependencies on their arguments. Implicit compiler helper types
are excluded; top-level statements and assembly attributes still contribute
assembly/namespace dependencies and project ownership evidence.

`DependencyEvidence` retains `SourceProject`, optional `TargetProject`, source and
target symbol descriptions, an optional `ProjectReference`, and a `DiagnosticEvidence`
fact. Its source coordinates are one-based with an exclusive end, as defined by
the [shared contracts](shared-contracts.md#locations-immutability-and-reproducibility). Project
references retain both project paths without inventing a source line for evaluated
or imported declarations. Semantic facts retain the bound source symbol and actual
syntax span. Facts can be reused in a future rule's violation evidence.

For example, Orders referencing Billing.Infrastructure creates a project edge with
the reference path, and using `VisibleStore` creates semantic edges with the source
member and location. The aggregated Orders → Billing module edge retains both
facts. A rule can inspect each fact's project pair through
`Repository.ResolvedPolicy.AllowsModuleDependency` or `AllowsLayerDependency`.
Keeping project pairs matters when one target is a permitted public contract and
another target in the same module is forbidden implementation access.

Aggregation includes direct edges only. It suppresses intra-module/intra-layer
self edges and excludes external metadata. It does not infer transitive permissions,
evaluate rules, assign severities or render reports.

## Stable identities and ordering

Graph node IDs hash the architectural kind and normalized identity. Source endpoints
reuse [repository identities](repository-model.md#identities-and-reproducibility).
External assembly identities include their full metadata assembly identity; namespace
and type endpoints are scoped to that identity. Edge IDs hash their directional
endpoint IDs independently of evidence. Duplicate edges merge all distinct facts in
canonical ordinal order. Conflicting node data for the same identity is rejected.

Authored evidence paths are relative to the policy directory with `/` separators;
linked sources can retain `..`. Generated evidence paths use
`generated/PROJECT/GENERATED_FILE_ID/HINT`, where the generated-file ID comes from
the generator assembly/type/hint, excluding intermediate directories and physical
checkout paths. Generated and authored file-local declarations share the same
identity normalization as the repository builder.

`Nodes` and `Edges` are immutable and ordered by ordinal graph/edge identity.
`Outgoing` orders neighbors by target identity. `Traverse` visits reachable nodes
breadth first, including the start, once each. `FindCycles` returns cyclic strongly
connected components, including self loops, rather than enumerating exponentially
many simple cycles. Each component and the component list have deterministic ordinal
ordering; the member order is not an executable cycle path. Consumers can select the
corresponding edges and their evidence from the graph. Traversal and cycle discovery
are iterative and support cancellation.

## Coverage

Completion requires a repository, a graph and no operational issues. An available
graph does not establish complete coverage or policy compliance. Missing workspace
context, invalid ownership, compiler errors, unresolved types/references, ambiguous
assembly ownership and unmapped source symbols prevent completion. Unbound dynamic
operations produce `dependency-dynamic-unresolved` issues with source spans.
Extraction retains available edges when semantic coverage is incomplete. Requested
cancellation returns `Cancelled`; an unexpected extraction exception returns
`Failed`, with available evidence retained. Hosts must propagate this coverage into
rule evaluation and gates.

The graph records static dependencies. It does not infer reflection targets,
runtime dependency-injection registration effects or concrete virtual-dispatch
targets. A virtual/interface invocation records the statically bound declaring type.
No runtime call-graph coverage is claimed.

Tests cover traceable forbidden dependencies, each graph level, semantic forms,
file-local and generated identities, checkout relocation, configuration/output-path
parity, incomplete semantic context, cancellation, deterministic cyclic components,
immutable evidence and deep acyclic graphs. Existing architecture boundary checks
continue to enforce Core's independence from compiler and presentation libraries.
