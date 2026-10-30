# Policy host composition

## Host composition

`DependencyGraphBuilder` composes workspace normalization, policy resolution
and extraction into a neutral `DependencyBuildResult`, as shown in the
[dependency graph guide](dependency-graph.md#build-a-graph). The AG001–AG006 rules
consume that result directly, preserving upstream coverage and issues alongside findings.
For a policy selecting any of these rules, a host can compose evaluation as follows:

```csharp
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Rules;

// context is the DependencyBuildResult returned by DependencyGraphBuilder.
if (context.Repository is null)
{
    // Surface context.Status and context.Issues; no evaluation context is available.
    return;
}
var policy = context.Repository.Policy;
IArchitectureRule<DependencyBuildResult>[] availableRules =
    [new ModuleDependenciesRule(), new LayerDependenciesRule(), new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()];
var selected = policy.SelectRules(availableRules);
var service = new AnalysisService<DependencyBuildResult>(selected);
var result = await service.AnalyzeAsync(
    new AnalysisRequest<DependencyBuildResult>(context, policy.SeverityPolicy), cancellationToken);
var passesGate = policy.Gate.Passes(result);
```

`RepositoryModel` carries the immutable policy and resolved mappings where rules
need them. A caller can also resolve a supplied complete project inventory directly
with `ResolvedPolicy`, without a workspace. Hosts reference Configuration and Analysis
at composition time; Analysis and Core do not reference the YAML adapter. The CLI
provides this composition through `scan --policy <path>` and reports resolved
ownership through `inspect --policy <path>`; see the [CLI guide](cli.md).

The supplied inventory and graph establish AG001–AG006 scope; omission from `rules`
disables a rule's selection. V1 rejects `enabled` and per-rule scope fields. AG001 uses
project pairs from the graph's evidence, so approved contracts never mask forbidden
implementation dependencies inside the same aggregated module edge. Unmapped pairs
prevent completed coverage. See the [AG001 guide](rule-catalog.md#ag001-module-dependencies).
AG002 independently checks the configured layer matrix and retains symbol/source
evidence. See the [AG002 guide](rule-catalog.md#ag002-layer-dependencies).
AG003 independently detects cycles even when every dependency has module or contract
permission. See the [AG003 guide](rule-catalog.md#ag003-module-cycles). The repository policy selects all six rules; the introductory modular-monolith
example selects AG001–AG003. AG004 checks semantic implementation access
independently of module permissions. AG006 checks configured domain technology
restrictions and scoped exceptions.

`PolicyConfigurationException` reports a document path, one-based line/column,
logical field path and corrective message. Contract/reference errors may point to
the containing declaration or document and name the specific identity in the
message. Read errors and requested cancellation propagate to the host. Resolution
errors are `ArgumentException` values identifying an invalid inventory/policy pairing.
Hosts must surface these failures rather than substitute empty policy or clean
analysis results.
