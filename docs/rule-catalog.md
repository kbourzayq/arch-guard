# Rule catalog

Architecture Guardian implements six canonical rules. CLI and MCP solution scans
can evaluate all six; compiler analysis implements AG001, AG002, AG004, AG005 and
AG006 against its host compilation. Only the solution engine checks module cycles.

| Rule | Check |
| --- | --- |
| [AG001](#ag001-module-dependencies) | Module and public-contract dependency permissions |
| [AG002](#ag002-layer-dependencies) | Layer dependency directions |
| [AG003](#ag003-module-cycles) | Logical module cycles |
| [AG004](#ag004-implementation-boundaries) | Cross-module implementation access |
| [AG005](#ag005-forbidden-references) | Explicit assembly and namespace restrictions |
| [AG006](#ag006-domain-infrastructure) | Layer-scoped technology restrictions and exceptions |

Choose rules and severities in your [YAML policy](policy.md#rule-selection-severity-and-gates).
Use CLI `explain <rule-id>` for local metadata. The
[semantic accuracy matrix](semantic-accuracy.md) records scope, precision and limits;
the [compiler guide](compiler-analyzer.md) describes shared YAML policy, legacy XML migration and severity setup.

## How catalog metadata reaches a diagnostic

Each rule has a stable ID, title and documentation link. A selected rule evaluates
observed dependency evidence against its corresponding policy constraint. A finding
then receives its configured severity; completed analysis evaluates the gate.
The tool uses local compiled rules and does not fetch checking logic or help pages.

Use `archguard explain AG001` for rule metadata without loading a workspace.
For a current MCP diagnostic, `explain_violation` can return instance-specific
evidence and corrective guidance. See [diagnostics and outcomes](concepts/diagnostics.md).
The six rule headings below are published help anchors used by diagnostics and reports.
Each example describes that rule's decision; full compliance requires all selected
checks, completed coverage and a passed gate.

## AG001: Module dependencies

Detect dependencies forbidden between logical modules by policy.

### AG001 examples and remediation

Given the [complete policy example](policy.md#document-shape), Orders permits only
`contract:Billing/api`, whose project is `src/Billing/Billing.Contracts.csproj`:

```text
Valid:   Orders.Application -> Billing.Contracts      (declared api contract)
Valid:   Orders.Application -> Orders.Domain          (same module)
Invalid: Orders.Application -> Billing.Infrastructure (no permission)
Invalid: Billing.Infrastructure -> Orders.Domain      (no reverse permission)
```

These arrows represent observed project references or bound uses, not YAML syntax.
Even an unused evaluated project reference can supply AG001 evidence. Inspect the
source/target project paths in the finding before changing permissions.

Remove the dependency or depend on the target's declared contract instead. Add a
precise `contract:Billing/api` permission only if that direction is intended.
`module:Billing` grants AG001 access to every Billing project, but AG004 can still
reject implementation uses. Permissions are not inherited through another module.

### AG001 evidence and limitations

Same-module dependencies pass. Cross-module dependencies require an outgoing
`module:ID` permission or the target project's exact `contract:MODULE/ID`
permission. Permissions are direct and directional; approval for one contract
does not authorize another contract or implementation project. External metadata
has no target project and is outside AG001's module ownership checks.

The rule reports one finding per forbidden source/target project pair, naming both
modules and project paths. Findings retain all distinct project-reference and
semantic facts, source spans and corrective guidance. The first located fact in
canonical evidence order supplies the primary location; reference-only findings
have no invented line number. Repeated facts across graph levels are deduplicated.
Original project ownership is used even when a module edge combines approved
contract access with forbidden implementation access. Aggregate edges are optional;
the same evaluation works over direct project or semantic evidence.
Evaluated compiler references can include SDK-expanded transitive project references;
AG001 checks the facts present in the graph and does not compute a transitive closure.

Rule omission controls enabled state through policy selection. Severity is assigned
after discovery, and gates remain separate. V1 has no `enabled` switch or per-rule
scope selectors. The supplied repository inventory and graph define scope. Graph
evidence referencing projects outside that inventory produces an AG001
`dependency-project-unmapped` issue and prevents completed coverage. Loading,
normalization and extraction status/issues propagate through evaluation, retaining
available findings without claiming a clean result from partial input. Cancellation
is cooperative. AG001 does not enforce layers, cycles or symbol visibility.

## AG002: Layer dependencies

Detect dependencies forbidden between architectural layers by policy. AG002 denotes
the layer rule. Earlier illustrative CQRS use of AG002 is superseded by this
canonical layer rule; AG002 does not enforce CQRS conventions.

### AG002 examples and remediation

In the [complete policy example](policy.md#document-shape), Application permits
Domain and Contracts; Domain has no outgoing layer permissions:

```text
Valid:   Orders.Application -> Orders.Domain     (Application allows Domain)
Valid:   Orders.Application -> Billing.Contracts (Application allows Contracts)
Invalid: Orders.Domain -> Orders.Application     (Domain denies Application)
```

A same-module relationship does not make the last edge valid. AG002 can report it
even when the project reference has no C# use. Evidence names the original project
pair and layers, with source spans when semantic uses are available.

Remove or invert the edge: define an interface in Domain and implement it in
Application, so Domain no longer references Application. Change `allowedDependencies`
only when the intended layer direction actually changes. A module/contract grant
cannot repair a layer violation.

### AG002 evidence and limitations

Layer names and the outgoing
`allowedDependencies` matrix come entirely from policy: no universal Clean
Architecture layout or special handling of names such as Domain or Infrastructure
is assumed. For example, Domain → Infrastructure is forbidden when Infrastructure
is absent from Domain's outgoing list, and allowed when explicitly listed.

Same-layer dependencies pass, including across modules. Cross-layer dependencies
require the target layer's exact ID in the source layer's outgoing list; permissions
are direct and directional. Module or public-contract permissions cannot override
a layer restriction, and AG002 does not enforce module permissions. Select AG001
and AG002 together to enforce both dimensions.

Each forbidden project pair produces one finding naming both layers and project
paths, retaining the original reference facts, semantic symbol descriptions and
available source spans. Duplicate facts across graph levels are merged. The first
located fact in canonical evidence order supplies the primary location; reference-only
findings have no invented location. Remediation describes removing or inverting the
dependency, or explicitly changing the configured matrix when intended. The graph's
observed references are checked without computing a transitive closure.

The supplied inventory and graph define scope. External metadata has no repository
target project and passes this ownership check. Projects omitted from a prepared
scope are not inspected; evidence for a project absent from its inventory is instead
an AG002 `dependency-project-unmapped` coverage issue. V1 has no per-rule scope
selectors. Omit AG002 from `rules` to disable its selection; do not weaken inventory
validation to exclude projects. Upstream incomplete, failed and cancelled outcomes,
issues and available findings are preserved. Evaluation cooperates with cancellation.
Severity and gates apply separately after discovery. See
[policy selection and gates](policy.md#rule-selection-severity-and-gates).

## AG003: Module cycles

Detect cycles across logical modules using the solution-wide dependency graph.

### AG003 examples and remediation

Consider Billing.Application and Billing.Contracts owned by Billing, and
Orders.Application and Orders.Contracts owned by Orders. Contracts are declared
in their owning modules, and every cross-module edge has a contract permission:

```text
Valid:   Orders.Application -> Billing.Contracts
         Billing.Application -> Billing.Contracts
         Module graph: Orders -> Billing

Invalid: Orders.Application -> Billing.Contracts
         Billing.Application -> Orders.Contracts
         Module graph: Billing -> Orders -> Billing
```

The invalid project graph can build without a project-reference cycle. Both edges
can pass AG001 and AG004 while their logical modules form an AG003 cycle. A finding
shows a closed representative module path and reference/symbol evidence for its hops.

Remove or invert one cross-module edge, or extract the shared contract into an
independent module whose dependencies do not recreate the cycle. Adding permissions
or marking more projects as contracts does not remove a module edge. The runnable
[modular-monolith example](use-cases.md#modular-monolith-boundaries) includes a cycle scenario.

### AG003 evidence and limitations

AG003 uses each fact's original project ownership, so
project, semantic and aggregated graphs yield the same results. Repeated edges
and facts across graph levels are merged. External metadata has no repository
module and is excluded. The rule computes no transitive closure.

Self-dependencies within one module are excluded, including references between
its implementation and contract projects, reciprocal project references within
that module and explicit aggregate self edges supported by intra-module evidence.
AG003 detects cycles **between distinct modules**. Allowed `module:ID` and
`contract:MODULE/ID` dependencies still participate: permission to depend on a
contract does not exempt that edge from cycle detection. For example,
Billing.Application → Orders.Application → Billing.Contracts is a module cycle
Billing → Orders → Billing even though its project graph is acyclic.

Iterative strongly connected component discovery yields one finding per cyclic
component, including disconnected components. It avoids enumerating every simple
cycle. Each finding reports a closed, directed path such as `A → C → B → A`,
not a sorted member list. Starting at the component's ordinal-first module ID,
breadth first search selects a shortest return path; ordinal target module IDs
resolve equal-length ties. A representative path may omit other members of a
complex component. Input order, repeated edges and graph identity hashes do not
change the selected path or finding identity for the same snapshot.

Findings target the path's first module and retain all distinct original reference
and semantic facts for the chosen hops, with available source spans. Evidence for
other edges in the component is excluded. The first located fact in canonical
order supplies the primary location; reference-only cycles have no invented source
line. Remediation suggests removing or inverting an edge or extracting a shared
contract into an independent module.

Rule selection, reporting severity and gates follow the
[YAML policy](policy.md#rule-selection-severity-and-gates). The loaded inventory and graph
define scope; upstream incomplete, failed or cancelled coverage and available
findings survive evaluation. Unmapped project evidence produces an AG003
`dependency-project-unmapped` issue and prevents completed coverage. Missing context
cannot become a clean completed result. Graph construction and traversal observe
cancellation. Whole-module cycles require solution-wide scope; a single compiler
invocation cannot establish it. CLI `scan` and MCP `analyze_solution` compose this evaluation.

## AG004: Implementation boundaries

Detect access to another module's internal implementation across declared boundaries.

### AG004 examples and remediation

Assume `IInvoiceReader` is defined in Billing's declared `api` contract project and
`InvoiceStore` in its non-contract infrastructure project. These illustrative C#
uses occur inside Orders.Application:

```csharp
// Valid for AG004: the type belongs to a declared public-contract project.
public sealed class ReadInvoices(Billing.Contracts.IInvoiceReader reader) { }

// Invalid for AG004: C# public accessibility does not make a project a contract.
public sealed class ReadInvoicesDirectly(Billing.Infrastructure.InvoiceStore store) { }
```

A module permission for all of Billing does not authorize the second use. The
finding identifies the source/target projects, bound implementation symbol and use
spans. An unused project reference alone does not establish an AG004 violation.

Depend on the contract interface and keep implementation selection in an appropriate
composition layer. Move deliberately shared abstractions into a declared contract
project; do not expose the whole implementation project just to silence a finding.
Recheck AG001 permissions and AG002 layers after changing the dependency.

### AG004 evidence and limitations

AG004 evaluates bound C# uses between owned projects.
A bound type/member owned by another module must belong to a declared public-contract
project. Types in non-contract projects remain implementation even when C# declares
them `public`; namespaces, type names and accessibility do not declare contracts.
Same-module uses (including between projects) and external metadata pass. A module
with no contracts exposes no implementation to other modules when AG004 is selected.
Version 1 contracts cover whole projects, not individual types or namespace selectors.

AG004 is independent of AG001 permissions: `module:Catalog` permits a module dependency
for AG001 but does not allow AG004 access to `CatalogDbContext` in Catalog's implementation.
A declared contract use passes AG004 even if AG001 denies the dependency; select both
rules to enforce permission and encapsulation. AG002 independently enforces layers.

Unused project/assembly references and imports are not semantic implementation uses.
The solution rule groups findings by original source project, target project and bound
target symbol, preserving distinct source spans and descriptions, including aliases,
generic arguments, inheritance and uses in partial/generated declarations. Graph-level
and aggregate copies are deduplicated without mixing contract evidence into findings.
The first located fact in canonical order supplies the primary span. Symbol evidence
comes from the extractor's [semantic reference scope](concepts/dependency-analysis.md#semantic-reference-scope).

CLI `scan` runs AG004 when selected in the policy's `rules` mapping. Omit it to disable
selection; severity and gates remain separate. Upstream coverage/issues and partial
findings survive evaluation. Unmapped semantic project evidence reports an AG004
`dependency-project-unmapped` issue and prevents completed coverage. Cancellation is
cooperative.

The compiler adapter shares the same ownership predicate, ID, title and help URL,
reporting one finding per bound name. Its supported references, generated-code behavior
and limitations are specified in the [compiler guide](compiler-analyzer.md#reporting-and-scope).
Neither engine discovers runtime reflection or dynamic dispatch targets.

## AG005: Forbidden references

Detect assembly or namespace references forbidden by policy.

### AG005 examples and remediation

For a module with `forbiddenNamespaces: [System.Net]`, the following illustrative
members show bound uses; a containing type is omitted:

```csharp
// Invalid: System.Net.Http is a descendant of the forbidden namespace.
public System.Net.Http.HttpClient CreateClient() => new();

// Valid for this restriction: text is not a bound System.Net symbol use.
public string EndpointName => "System.Net.Http.HttpClient";
```

`System.NetLike` would not match `System.Net`; an unused `using System.Net.Http;`
also supplies no finding. For `forbiddenAssemblies`, use the bound assembly's exact
simple name, not a package ID or source alias. A type can match both an assembly
ban and a namespace ban, producing separate findings with each restriction named.

Replace the use with an allowed abstraction and put networking in an appropriate
adapter, or review the exact module/layer restriction if the dependency is intended.
Changing spelling through an alias or granting a module dependency does not change
the bound identity. Evidence retains that identity and the configured restriction.

### AG005 evidence and limitations

AG005 checks bound semantic evidence against the source module's and layer's `forbiddenAssemblies` and
`forbiddenNamespaces` lists. [Policy configuration](policy.md#forbidden-references-ag005)
defines exact assembly names and namespace descendant matching. There are no built-in
technology bans: framework, package, repository and same-project symbols are checked
only against explicit restrictions. Permissions and contracts never override a ban.

Aliases, global/static imports, generic arguments and partial/generated sources use
bound identities, rather than source spelling or diagnostic display strings. Unused
imports/references, strings, comments and allowed lookalikes do not create findings.
Each finding identifies the target symbol, source project and exact configured
module/layer restriction. The standalone engine groups by project, symbol and
restriction, deduplicating aggregate evidence and preserving source spans. Distinct
matching module/layer or parent/child restrictions produce independent findings.

The compiler adapter shares Core's matching, validation and metadata, reporting at
bound name use sites, including target assemblies outside its ownership snapshot.
Supported compiler forms and broader standalone extraction differ as documented in
the [compiler scope](compiler-analyzer.md#reporting-and-scope) and
[semantic graph scope](concepts/dependency-analysis.md#semantic-reference-scope); counts and
snapshot IDs need not match. Cross-engine tests use the same compilation for aliases,
framework types, generated sources, lookalikes and same-project namespace references.

Upstream coverage, issues and partial findings are preserved. Unmapped source projects
or semantic evidence missing bound assembly/namespace identities prevent completed
coverage. CLI `scan` runs AG005 only when selected; severity and gates apply after
discovery. No runtime reflection or dynamic target is inferred.

## AG006: Domain infrastructure

Detect domain code referencing infrastructure technology forbidden by policy.

### AG006 examples and remediation

The [complete policy example](policy.md#document-shape) configures Domain with
`forbiddenNamespaces: [System.Data]` and `allowedNamespaces: [System.Data.SqlTypes]`
inside `domainInfrastructure`. In an Orders.Domain type:

```csharp
// Invalid: a bound technology use forbidden in this source layer.
public System.Data.DataTable CreateTable() => new();

// Valid for AG006: an explicit namespace exception in this source layer.
public System.Data.SqlTypes.SqlInt32 EmptyValue => System.Data.SqlTypes.SqlInt32.Null;

// Valid for AG006: an ordinary domain value outside the configured bans.
public decimal Total => 0m;
```

An exception includes descendants at namespace-segment boundaries and also waives
AG006 assembly bans for that namespace. It does not waive AG005 or AG002. The
finding records the source project/layer, target symbol and exact technology ban.

Keep data access behind domain abstractions and implement it in an adapter. When
a dependency is an intentional domain value, review a narrowly scoped exception;
allowing a namespace authorizes every type/member in that subtree. Renaming the
layer to `Domain` alone enforces nothing: the configuration and AG006 selection
are both required.

### AG006 evidence and limitations

AG006 combines resolved source layer membership with bound
assembly and namespace identities. A layer opts in through
its [domainInfrastructure configuration](policy.md#domain-infrastructure-ag006),
independent of its name. No persistence package, framework type or namespace is banned
by default. Target namespace exceptions apply only to this source layer's AG006 bans;
domain abstractions outside those bans pass without an exception.

The solution rule groups by source project, target symbol, source layer and exact
restriction, merging duplicate facts across graph levels and retaining all distinct
source spans. Each matching assembly or namespace restriction yields its own finding;
input order does not change its identity. The first located fact in canonical order
supplies the primary location. Findings name the configured restriction and suggest
replacing the technology use with a domain abstraction or revising that restriction.
Unused references/imports are outside semantic-use scope. Missing source ownership or
bound identities produce coverage issues; upstream incomplete, failed and cancelled
outcomes and available findings survive. Cancellation is cooperative.

**Diagnostic deduplication and overlap:** deduplicate identical evidence within each
rule and restriction, with no cross-rule suppression. AG002 reports forbidden project
layer dependencies, including reference-only edges. AG005 reports independent general
module/layer reference bans. AG006 reports the domain technology contract. Selecting
all three can yield all three IDs for one use, because they enforce separate policies
and severities. An AG006 exception cannot silence AG002 or AG005. To avoid intentionally
redundant reference findings, express a domain technology ban under AG006 and reserve
AG005 for other general restrictions; omit rules only when their independent checks
are not required. Namespace and assembly bans that both match remain independent.

The compiler analyzer uses the same AG006 matching and metadata, emitting a
located diagnostic per bound name and restriction. Framework/external targets need no
ownership entry. See [compiler scope](compiler-analyzer.md#reporting-and-scope) for
explicit-name coverage and broader standalone semantic extraction. Cross-engine tests
exercise aliases, generic arguments, generated sources, framework identities, scoped
exceptions and lookalikes against the same compilation.

Severity is assigned separately from discovery. A rule's metadata does not decide
the quality gate; see [analysis outcomes](concepts/diagnostics.md).

## Compiler scope

The [compiler adapter](compiler-analyzer.md) evaluates AG001/AG002/AG004 for bound semantic
uses between assemblies present in its supplied policy and ownership metadata. AG005 and AG006 also check
explicit restrictions on all bound target assemblies. The adapter shares Core's
IDs, titles, help URLs and permission predicates. Its diagnostics carry per-use source
spans; solution diagnostics aggregate project-pair evidence. AG000 reports unavailable
compiler configuration and is an operational adapter diagnostic, outside AG001–AG006.
Compiler results do not establish whole-solution coverage or cycle compliance.
