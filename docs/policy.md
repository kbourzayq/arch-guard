# Architecture policy reference

`architecture.guardian.yaml` is the versioned source of architecture rules for
CLI and MCP analysis. It defines project ownership, modules, layers, public
contracts, dependency permissions, rule selection, severities and the quality gate.

New users can start with the [policy walkthrough](getting-started.md#2-write-a-policy).
This reference covers YAML version `1`; the
[JSON Schema](schema/architecture.guardian.v1.schema.json) supports editor validation.
The [repository policy](../architecture.guardian.yaml) maps this solution's eleven
projects, and the [modular-monolith example](examples/modular-monolith/architecture.guardian.yaml)
demonstrates public contracts. Both are loaded and resolved by tests.

CLI `scan` and MCP `analyze_solution` use the same loader, ownership resolution and
rule engine. Loading a valid policy alone establishes neither resolved project
coverage nor compliance. Compiler integration reads this same YAML with [build-supplied ownership metadata](compiler-analyzer.md).
Legacy XML remains supported during the transition release.

## Document shape

For a repository with the following four projects, place the policy beside `src/`:

```text
architecture.guardian.yaml
src/Orders/Orders.Application.csproj
src/Orders/Orders.Domain.csproj
src/Billing/Billing.Contracts.csproj
src/Billing/Billing.Infrastructure.csproj
```

This complete example selects all six rules. Project references and C# uses still
come from the loaded workspace; the YAML declares ownership and constraints only.

```yaml
version: 1
modules:
  - id: Orders
    projects: ["src/Orders/**/*.csproj"]
    allowedDependencies: ["contract:Billing/api"]
    forbiddenNamespaces: [System.Net]
  - id: Billing
    projects: ["src/Billing/**/*.csproj"]
    contracts:
      - id: api
        projects: ["src/Billing/Billing.Contracts.csproj"]
layers:
  - id: Domain
    projects: ["src/Orders/Orders.Domain.csproj"]
    domainInfrastructure:
      forbiddenNamespaces: [System.Data]
      allowedNamespaces: [System.Data.SqlTypes]
  - id: Contracts
    projects: ["src/Billing/Billing.Contracts.csproj"]
  - id: Application
    projects: ["src/Orders/Orders.Application.csproj"]
    allowedDependencies: [Domain, Contracts]
  - id: Infrastructure
    projects: ["src/Billing/Billing.Infrastructure.csproj"]
    allowedDependencies: [Contracts]
rules:
  AG001: error
  AG002: error
  AG003: warning
  AG004: error
  AG005: error
  AG006: error
gate:
  failOn: error
```

`version`, `modules`, `layers` and `rules` are required. The version is the integer
`1`. Modules, layers, project-selector lists and selected rules must be nonempty.
Module and layer IDs are unique within their respective collections; contract IDs
are unique within their module. IDs follow `[A-Za-z][A-Za-z0-9_.-]*` and compare
ordinally, including case. Module and layer IDs may share a spelling.

`contracts`, `allowedDependencies`, `forbiddenAssemblies` and `forbiddenNamespaces`
default to empty lists. Layers may also declare the optional
[`domainInfrastructure`](#domain-infrastructure-ag006) object. `gate` defaults to
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

Module identity is the explicit `modules[].id`, such as `Billing`. It is not inferred
from a directory, assembly name, `RootNamespace`, namespace or solution folder. Two
projects with different names can belong to one module; renaming an assembly does
not change module membership when the project path and policy stay the same.
Changing a module ID changes diagnostic targets and can make baseline findings new.
Project moves require updating selectors and references together.

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

## Forbidden references (AG005)

Modules and layers optionally declare `forbiddenAssemblies` and `forbiddenNamespaces`:

```yaml
modules:
  - id: Orders
    projects: ["src/Orders/**/*.csproj"]
    forbiddenAssemblies: [Legacy.Data]
    forbiddenNamespaces: [System.Net]
layers:
  - id: Domain
    projects: ["src/**/*.Domain.csproj"]
    forbiddenNamespaces: [Microsoft.EntityFrameworkCore]
rules:
  AG005: error
```

This fragment belongs in a complete v1 document. Both lists default to empty and may
be empty. Values must be unique. Assembly restrictions are exact, case-sensitive
simple names using ASCII letters, digits, `.`, `_` and `-`; no paths, versions,
wildcards or NuGet package IDs are interpreted. Namespace restrictions are
case-sensitive, dot-separated ASCII identifiers (letter or `_` first, then letters,
digits or `_`). Each restriction includes the exact namespace and its descendants:
`System.Net` matches `System.Net.Http`, while `System.NetLike` passes. Do not use
`global::`, `@` escapes or wildcards in policy names.

A project's module and layer restrictions apply independently; allowed dependencies,
contracts and same-module ownership do not waive them. Framework, package, repository
and same-project symbols are included. AG005 checks bound type/member uses, not
strings, comments, unused imports or unused assembly/project references. Framework
restrictions use their bound assembly identity, which can differ from a reference
facade or package name. Existing policies remain valid. Select AG005 in `rules` to
run it; declaring restrictions alone does not select a rule.

## Domain infrastructure (AG006)

A layer's optional `domainInfrastructure` object marks that layer as a source of
AG006 checks. Membership comes from its project selectors; no layer name (including
`Domain`) or technology is special. For example:

```yaml
layers:
  - id: Domain
    projects: ["src/**/*.Domain.csproj"]
    domainInfrastructure:
      forbiddenAssemblies: [Legacy.Persistence]
      forbiddenNamespaces: [Microsoft.EntityFrameworkCore, System.Data]
      allowedNamespaces: [System.Data.SqlTypes]
rules:
  AG006: error
```

Use this fragment inside a complete v1 document. All three lists are optional,
unique and may be empty. Names and descendant matching follow AG005's exact assembly
and namespace rules above. An absent or empty object bans nothing. `allowedNamespaces`
exempts bound target namespaces and descendants from **both** AG006 assembly and
namespace bans, only for projects in this source layer. It does not exempt similarly
prefixed namespaces or another layer. Namespace exceptions authorize all types and
members in that namespace subtree, regardless of assembly; use a narrow namespace.
Allowed domain abstractions outside the configured technology identities pass.

AG006 checks bound type/member evidence, including framework, package, repository and
same-project symbols. It does not ban a technology merely because an unused reference
or import exists. Module permissions, contracts and the layer dependency matrix do
not waive these restrictions. Select AG006 to execute it; declaring the object alone
does not select it. Exceptions do not override AG002 or AG005; see the
[overlap policy](rule-catalog.md#ag006-domain-infrastructure).

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
CLI `scan --baseline <path>` applies the same threshold only to new findings.
Baseline files are explicit command inputs; YAML v1 has no baseline setting.
See [diagnostic baselines](baselines.md).

## Baselines and CLI exit codes

Accepted debt is a separate, explicitly reviewed JSON inventory. Choose the gate
mode at the command line; none of these modes permits missing coverage:

| Command mode | Findings that can fail the severity gate |
| --- | --- |
| `scan` | All current findings |
| `scan --baseline <file>` | New findings after stable fingerprint matching against the explicit file |
| `scan --base <commit-or-ref>` | New findings compared with a complete scan of the local Git base |
| `baseline update ... --baseline <file>` | Saves all current findings after completed analysis; success accepts the inventory rather than proving a clean architecture |

`--base` and `--baseline` cannot be combined. Scans never update accepted debt.
Review changes to rule selection, ownership and baseline entries together; use
separate baseline files for independently analyzed configurations/frameworks.
See [baseline matching and updates](baselines.md) and [Git comparison](changes.md).

| CLI exit | Meaning |
| --- | --- |
| `0` | The command completed successfully; a scan passed its configured gate, or an explicit baseline update saved its inventory |
| `1` | The scan completed and a finding failed the applicable severity gate |
| `2` | Invalid arguments/policy/baseline, unavailable context or rule, incomplete/failed analysis, cancellation or output failure |

An `error` threshold can pass warnings, and accepted existing errors can pass a
new-finding gate. Neither result means there are no findings. Incomplete analysis
with zero findings exits `2`. MCP exposes completion and gate outcome separately;
compiler severity comes from editorconfig instead of this CLI exit contract.

## Execution and reproducibility

Policy loading and Core evaluation do not fetch rules, schemas or help pages.
The workspace must already have the SDK, reference packs, restored assets and
generator inputs for the selected configuration/framework. See
[offline and deterministic operation](workspace-loading.md#offline-and-deterministic-operation)
for preparation and the limits of reproducibility, and
[trust and isolation](workspace-loading.md#trust-and-isolation) before loading an
untrusted repository. YAML restrictions constrain analyzed dependencies; they do
not sandbox MSBuild tasks or source generators.

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

## Compiler policy distribution

The netstandard2.0 [compiler analyzer](compiler-analyzer.md) reads this same YAML
through AdditionalFiles, using packaged MSBuild targets for project/assembly
ownership metadata. It shares strict parsing and selector semantics with CLI/MCP.
Selected compiler-supported rules use YAML default severities; `.editorconfig` can
override or suppress their reporting. AG003 and the solution gate remain CLI/MCP
responsibilities. Legacy XML is supported for one transition release; configure
exactly one format and follow the [migration guide](compiler-analyzer.md#yaml-ownership-and-migration).
