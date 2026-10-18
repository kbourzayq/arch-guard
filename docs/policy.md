# Architecture as Code: policy v1

[F03](https://github.com/kbourzayq/arch-guard/issues/3) defines
`architecture.guardian.yaml`, the [v1 JSON Schema](schema/architecture.guardian.v1.schema.json),
the YAML loader and pure policy resolution. The [repository policy](../architecture.guardian.yaml)
maps this solution's six projects. The [modular-monolith example](examples/modular-monolith/architecture.guardian.yaml)
illustrates public contracts. Both are loaded and resolved by tests.

The loader and policy APIs, [trusted workspace loading](workspace-loading.md),
[dependency extraction](dependency-graph.md), [AG001 evaluation](rule-catalog.md#ag001-module-dependencies),
[AG002 evaluation](rule-catalog.md#ag002-layer-dependencies) and
[AG003 evaluation](rule-catalog.md#ag003-module-cycles) are usable today.
F10 exposes these services through [CLI `scan`, `inspect` and `explain`](cli.md).
Remaining rules follow in their roadmap features. Loading a valid policy alone does
not establish scan coverage or compliance.

## Document shape

```yaml
version: 1
modules:
  - id: Orders
    projects: ["src/Orders/**/*.csproj"]
    allowedDependencies: ["contract:Billing/api"]
  - id: Billing
    projects: ["src/Billing/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Billing/Billing.Contracts.csproj"]
layers:
  - id: All
    projects: ["src/**/*.csproj"]
rules:
  AG001: error
  AG003: warning
gate:
  failOn: error
```

`version`, `modules`, `layers` and `rules` are required. The version is the integer
`1`. Modules, layers, project-selector lists and selected rules must be nonempty.
Module and layer IDs are unique within their respective collections; contract IDs
are unique within their module. IDs follow `[A-Za-z][A-Za-z0-9_.-]*` and compare
ordinally, including case. Module and layer IDs may share a spelling.

`contracts` and `allowedDependencies` default to empty lists. `gate` defaults to
`failOn: error`; when present it must contain `failOn`. Duplicate list values,
duplicate YAML keys, missing fields, unknown fields and unknown rule IDs are errors.
No policy inheritance, environment expansion, implicit exclusion or fallback policy
is supported. Settings such as `extends`, `enabled`, `off`, baselines,
`allowIncomplete` and custom rule options are rejected instead of ignored.

The schema supports editor validation through a comment such as:

```yaml
# yaml-language-server: $schema=./docs/schema/architecture.guardian.v1.schema.json
```

The adapter enforces the structural schema in code, then validates identities and
references in Core. Editor JSON Schema validation cannot establish referential
integrity, selector coverage or ownership; always load **and resolve** before using
a policy. YAML must be one tree-shaped data document. Aliases, anchors, merge keys,
explicit tags and YAML version directives are unsupported. Quote glob strings;
strings resembling null, booleans or numbers also require quotes. The loader accepts
at most 32 nesting levels and 100,000 parser events and performs no network calls.

## Project identities and globs

The host supplies the complete inventory for the requested analysis scope. Each
identity is a project file path **relative to the directory containing the policy**.
The identity is not the assembly name, MSBuild project name, namespace or a path
relative to the current working directory. For the example above,
`src/Billing/Billing.Contracts.csproj` is the contract project identity.

The loader takes an explicit file path without directory discovery. It does not
read projects. The workspace adapter must calculate relative project paths and
preserve their case. Incoming inventory backslashes normalize to `/`; policy
selectors always use `/`. Leading/trailing slashes, drive prefixes, empty segments,
`.` and `..` segments and control characters are rejected. A project outside the
policy directory cannot be represented by a parent traversal.

| Selector | Meaning |
| --- | --- |
| `src/Billing/Billing.csproj` | Exact entire path |
| `src/Billing/*.csproj` | `*` matches zero or more characters within that segment |
| `src/Billing/Billing?.csproj` | `?` matches exactly one character within that segment |
| `src/**/*.csproj` | A whole `**` segment matches zero or more directories, including `src/App.csproj` |
| `src/**` | All project paths below `src/` |

Matching is anchored to the whole path and is ordinal/case-sensitive on Linux,
Windows and macOS. Dots and other permitted characters are literal. Character
classes, brace expansion, negation and embedded `**` such as `**.csproj` are errors.
There is no implicit `**/` prefix, extension filter, path case folding, symlink
resolution or filesystem enumeration. Selectors in one declaration form a union;
matching two selectors within the same declaration does not create two owners.

## Module, layer and contract resolution

Every inventory project must map to **exactly one module and exactly one layer**.
Modules and layers are independent dimensions: one module may span several layers,
and one layer may contain projects from several modules. Use a single `All` layer
if no finer layer distinction is needed.

Every declared selector must match at least one inventory project. Duplicate
normalized inventory paths, an empty inventory, unmatched projects, overlapping
modules or overlapping layers are errors. There is no first-match precedence,
automatic module creation or silent ignore behavior. Missing projects due to loading
failures must remain a failed/incomplete host outcome; a reduced inventory must not
be represented as complete.

A public contract is an explicitly named **set of whole projects** within its module.
Every selected contract project must also belong to that module. A project may
belong to zero or one contract; overlapping contracts and selectors outside the
owning module are errors. A project without a contract is an implementation project.
V1 does not infer contracts from names such as `*.Contracts`, or define namespace,
type or member selectors. Those cannot be substituted for project identities.

Resolution produces immutable `ProjectPolicyMapping` values, sorted by project
identity. Declarations and input order do not change ownership or permissions.
Errors include the affected project/selector and module/layer context.

## Dependency permissions

Permissions are outgoing, direct and case-sensitive; they do not imply reciprocal
or transitive access. Omission means no cross-boundary permission. References must
resolve to declarations when loading the policy, even before an inventory exists.

| Permission | Resolution and effect |
| --- | --- |
| `module:Billing` | Exact declared module; permits AG001 project dependencies to all its projects, including implementation projects |
| `contract:Billing/api` | Exact declared module plus local contract ID; permits AG001 dependencies only to projects in that contract |
| `Contracts` in a layer's `allowedDependencies` | Exact declared layer ID; permits AG002 dependencies to projects mapped to that layer |

Bare module names, globs and undeclared targets in module permissions are errors.
`contract:Billing/api` does not grant access to another `api` contract, to other
Billing projects or to Billing's dependencies. Intra-module and intra-layer project
dependencies are allowed by their respective permission checks. Cycles are a
separate rule (AG003); an explicit permission does not disable cycle discovery.

`ResolvedPolicy.AllowsModuleDependency` and `AllowsLayerDependency` answer separate
questions about an **already discovered** project edge. A module permission cannot
override a layer restriction. Unknown source/target project identities are errors.
These APIs do not discover edges, produce violations, enforce semantic symbol
access or decide a quality gate. AG001's `ModuleDependenciesRule` attaches evidence
and findings to module permission decisions; AG002's `LayerDependenciesRule` does
the same for layer permission decisions. Neither assumes fixed layer names or a
universal Clean Architecture layout.

## Rule selection, severity and gates

The `rules` mapping explicitly selects canonical IDs AG001–AG006. Omitted IDs are
not selected. Every selected ID has exactly one severity: `info`, `warning` or
`error`. Selection is separate from reporting severity; `off` and `none` are invalid
severities. The [rule catalog](rule-catalog.md) describes each identity.

`ArchitecturePolicy.SelectRules` selects registered implementations in ordinal ID
order and rejects unavailable selected IDs **before analysis**. Catalog metadata is
not an implementation. A host must never drop unavailable rules and present the
remaining analysis as completed. Selecting a future rule in a valid document does
not mean the current engine can execute it.

Rules discover severity-free violations. `SeverityPolicy` assigns their configured
reporting levels after discovery without changing their identity or evidence.
`GatePolicy` then evaluates the completed result separately. Its `failOn` threshold
is inclusive: `warning` fails on warnings and errors; `info` fails on any diagnostic;
`error` fails on errors. Incomplete, failed and cancelled analysis always fails the
gate, regardless of threshold or diagnostic count. An empty completed result passes.
F22 will add baseline-aware gate semantics; v1 has no baseline setting.

## Host composition

F06's `DependencyGraphBuilder` composes workspace normalization, policy resolution
and extraction into a neutral `DependencyBuildResult`, as shown in the
[dependency graph guide](dependency-graph.md#build-a-graph). The AG001–AG003 rules
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
    [new ModuleDependenciesRule(), new LayerDependenciesRule(), new ModuleCyclesRule()];
var selected = policy.SelectRules(availableRules);
var service = new AnalysisService<DependencyBuildResult>(selected);
var result = await service.AnalyzeAsync(
    new AnalysisRequest<DependencyBuildResult>(context, policy.SeverityPolicy), cancellationToken);
var passesGate = policy.Gate.Passes(result);
```

`RepositoryModel` carries the immutable policy and resolved mappings where rules
need them. A caller can also resolve a supplied complete project inventory directly
with `ResolvedPolicy`, without a workspace. Hosts reference Configuration and Analysis
at composition time; Analysis and Core do not reference the YAML adapter. F10
provides this composition through `scan --policy <path>` and reports resolved
ownership through `inspect --policy <path>`; see the [CLI guide](cli.md).

The supplied inventory and graph establish AG001–AG003 scope; omission from `rules`
disables a rule's selection. V1 rejects `enabled` and per-rule scope fields. AG001 uses
project pairs from the graph's evidence, so approved contracts never mask forbidden
implementation dependencies inside the same aggregated module edge. Unmapped pairs
prevent completed coverage. See the [AG001 guide](rule-catalog.md#ag001-module-dependencies).
AG002 independently checks the configured layer matrix and retains symbol/source
evidence. See the [AG002 guide](rule-catalog.md#ag002-layer-dependencies).
AG003 independently detects cycles even when every dependency has module or contract
permission. See the [AG003 guide](rule-catalog.md#ag003-module-cycles). The repository
and modular-monolith example policies select AG001–AG003 and can now be evaluated
through this composition. Policies selecting AG004–AG006 still require those implementations.

`PolicyConfigurationException` reports a document path, one-based line/column,
logical field path and corrective message. Contract/reference errors may point to
the containing declaration or document and name the specific identity in the
message. Read errors and requested cancellation propagate to the host. Resolution
errors are `ArgumentException` values identifying an invalid inventory/policy pairing.
Hosts must surface these failures rather than substitute empty policy or clean
analysis results.
