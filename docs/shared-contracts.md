# Shared rule, diagnostic and analysis contracts

Architecture Guardian separates neutral contracts in `ArchitectureGuardian.Core`
from orchestration and workspace integration in `ArchitectureGuardian.Analysis`.
The contracts describe requests, findings, diagnostics, completion and cancellation;
Core uses only the .NET base library.

The [policy guide](policy.md) describes ownership, severity and `GatePolicy`.
YAML parsing stays in Configuration; [workspace loading](workspace-loading.md) and
[repository normalization](repository-model.md) stay in Analysis. See the
[architecture overview](architecture/overview.md) for component responsibilities
and the separate solution and compiler execution paths.

## Evaluation, severity and gates

`IArchitectureRule<TContext>` exposes a `RuleDescriptor` and an asynchronous
`EvaluateAsync` method. A rule receives a stable, neutral context and a cancellation
token. It returns a `RuleEvaluationResult` containing severity-free `RuleViolation`
values, an analysis status and any operational issues. Rule IDs come from the
[canonical catalog](rule-catalog.md). Descriptors can also represent future rule
packs without changing the Core contracts.

Every violation includes its rule, message, affected `DiagnosticTarget`, supporting
evidence and remediation. At least one module or symbol and one evidence item are
required. The rule descriptor provides an absolute HTTP/HTTPS documentation URL.
`Diagnostic` exposes these values directly alongside severity and identity.

The canonical solution evaluators also retain an immutable `RuleViolation.Context`.
`ViolationContext.DependencyPath` preserves directed endpoint order: project paths
for AG001/AG002, the representative module cycle (including the closing module)
for AG003, project/project/symbol for AG004, and project/symbol for AG005/AG006.
`PolicyConstraints` identifies symbolic scope, setting and relevant configured
values. AG003 instead identifies its built-in `rule:AG003` / `acyclicModules`
invariant. `PublicContracts` lists declared alternatives for the target module of
AG001/AG004; these do not promise a suitable replacement API or independent
module/layer permission. Provenance is captured during evaluation, never recovered
by parsing display text. Its addition leaves the v1 diagnostic identity unchanged;
custom findings may omit it. See [MCP explanations](mcp.md#explain_violation).

`SeverityPolicy` supplies per-rule overrides and a default severity (`Error` unless
configured otherwise). The supported severities are `Info`, `Warning` and `Error`.
Rule IDs use ordinal, case-sensitive matching. Changing severity does not rerun or
change a rule finding's identity. Policy files explicitly select rule IDs and
their severities. Baseline classification is separate from severity assignment; see [baselines](baselines.md).

A completed run may contain error diagnostics. A consumer must check coverage
status before applying quality-gate policy. `GatePolicy.Passes` fails
noncompleted results and applies an inclusive severity threshold to completed results.
`AnalysisResult` itself has no combined success flag or CLI exit code.

## Reusable service

`AnalysisService<TContext>` implements `IAnalysisService<TContext>` for both CLI and
MCP composition. It accepts registered rules and an `AnalysisRequest<TContext>`
with the prepared context and severity policy. For example, a host can compose:

```csharp
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;

static Task<AnalysisResult> AnalyzeAsync<TContext>(
    TContext snapshot,
    IEnumerable<IArchitectureRule<TContext>> rules,
    CancellationToken cancellationToken) where TContext : notnull
{
    IAnalysisService<TContext> service = new AnalysisService<TContext>(rules);
    var policy = new SeverityPolicy(new Dictionary<string, DiagnosticSeverity>
    {
        [RuleCatalog.ModuleDependencies.Id] = DiagnosticSeverity.Warning,
    });

    return service.AnalyzeAsync(new AnalysisRequest<TContext>(snapshot, policy), cancellationToken);
}
```

The host supplies actual rule implementations and the neutral model appropriate to
their scope, such as `DependencyBuildResult` for AG001–AG006, including the
repository, graph and extraction coverage. The context must remain stable throughout
evaluation; the service cannot defensively copy an
arbitrary context. Workspace/transport objects must be converted at adapter boundaries.

Rules execute sequentially in ordinal rule-ID order. Registration is copied and
duplicate IDs are rejected. A rule emitting another rule's ID fails the analysis.
Exceptions during rule evaluation become operational issues retaining the rule ID,
exception type and message. Invalid construction/registration arguments throw argument
exceptions so a composition error can be fixed by the caller.

### Service execution flow

This sequence describes the implemented service with a supplied neutral context
and rule instances. It shows the path where each rule completes; the
[outcome flow](#outcome-flow) covers other outcomes. CLI `load` validates workspace
context separately. CLI `scan` composes the reusable AG001–AG006 API with policy
loading, dependency extraction and gate evaluation; see the [CLI guide](cli.md).
All six canonical rules are available through this composition.

`SolutionAnalysisService.AnalyzeAsync` shares the complete scan pipeline
between CLI `scan` and MCP `analyze_solution`: policy selection before build
execution, workspace loading, dependency extraction and `AnalysisService` evaluation.
Hosts still own policy loading, gate rendering and protocol/process behavior.
`AnalyzeChangesAsync` accepts workspace/build/trust context, explicit policy and Git
base, a host-supplied `IPolicyLoader` and an optional operator root. Analysis captures
full snapshots, prepares their assets with cache-only restore and runs the shared
scan pipeline independently with each tree's policy. `ChangeAnalysisResult` retains
combined coverage, candidate diagnostics, the resolved base commit and a
`BaselineComparison` when the base completed. The candidate severity gate applies
only to new fingerprints, and neither tree's missing coverage can pass. Core
fingerprint/gate contracts stay pure; Git, processes and snapshot I/O stay in Analysis.
See [change analysis](changes.md) and the [MCP guide](mcp.md#analyze_changes).

```mermaid
sequenceDiagram
    participant Caller as Caller
    participant Service as AnalysisService
    participant Rule as Registered rule
    participant Policy as SeverityPolicy

    Caller->>Service: Construct with rule instances
    Note over Service: Copy registrations<br/>Reject duplicate IDs<br/>Sort by ordinal rule ID
    Caller->>Service: AnalyzeAsync(request,<br/>cancellationToken)
    Note over Caller,Service: Stable neutral context<br/>and severity policy in request
    loop Rules in ordinal ID order
        Service->>Rule: EvaluateAsync(context,<br/>cancellationToken)
        Rule-->>Service: RuleEvaluationResult<br/>(status, violations, issues)
        Note over Service: Validate finding rule IDs
        loop Each rule violation
            Service->>Policy: GetSeverity(ruleId)
            Policy-->>Service: Override or default severity
            Note over Service: Create Diagnostic<br/>from finding and severity
        end
        Note over Service: Retain issues<br/>Check cancellation<br/>and coverage outcome
    end
    Note over Service: Snapshot collections<br/>and sort canonically
    Service-->>Caller: AnalysisResult<br/>(status, diagnostics, issues)
```

Rules receive the context and cancellation token, without access to the request's
severity settings through this interface. Severity is applied after evaluation.
The returned `AnalysisResult` carries data. Later reporting adapters render it, while
hosts apply the separate quality-gate policy.

Rule descriptors and their help URLs travel with the findings. The
[catalog flow](rule-catalog.md#how-catalog-metadata-reaches-a-diagnostic) explains why
this requires no documentation fetch during analysis.

## Coverage and cancellation

| Status | Meaning |
| --- | --- |
| `Completed` | All registered rules completed against the supplied context; no operational issues |
| `Incomplete` | Coverage is missing; explanatory issues are required, and partial diagnostics remain available |
| `Failed` | An operational failure prevented completion; explanatory issues and prior diagnostics remain available |
| `Cancelled` | Work stopped for cancellation; available partial diagnostics and issues remain available |

`AnalysisResult.IsComplete` checks only for `Completed`. Empty diagnostics on an
incomplete, failed or cancelled result never establish a clean analysis. With no
registered rules the service returns `Incomplete` and a `no-rules` issue.

An incomplete rule does not prevent evaluating subsequent rules. A failed or cancelled
rule stops the run. The service checks cancellation before evaluation, between rules
and after each awaited result, forwards the supplied token, and converts requested
`OperationCanceledException` into `Cancelled`. An unrelated cancellation exception
is a failure. Cancellation before starting invokes no rules. Rules must observe the
token for prompt cancellation while running; the service does not abandon their tasks.

`AnalysisIssue` carries an operational code, message and optional rule/source location,
separately from policy violations. `WorkspaceLoadResult` uses the same coverage
statuses and issue contracts for loading. Hosts must propagate missing workspace
context rather than pass a silently reduced snapshot to rule evaluation as complete.
`RepositoryBuildResult` retains loading issues alongside normalization coverage;
hosts must inspect it before using its neutral model as a rule context.

### Outcome flow

This diagram summarizes the service's coverage decisions. All returned outcomes
retain the findings and operational issues collected before returning.

```mermaid
flowchart TD
    Start["Analysis request"] --> Check["Check cancellation<br/>and rule registrations"]
    Check -->|"Cancellation requested"| Cancelled["Cancelled"]
    Check -->|"No rules"| Incomplete["Incomplete<br/>Explanatory issues required"]
    Check -->|"Rules available"| Evaluate["Evaluate next rule<br/>Retain findings and issues<br/>Remember coverage gaps"]
    Evaluate -->|"Completed or Incomplete<br/>More rules remain"| Evaluate
    Evaluate -->|"Exception or Failed"| Failed["Failed<br/>Explanatory issues required"]
    Evaluate -->|"Requested cancellation<br/>or Cancelled"| Cancelled
    Evaluate -->|"No rules remain<br/>Coverage gaps"| Incomplete
    Evaluate -->|"No rules remain<br/>Full coverage"| Completed["Completed<br/>No operational issues"]
```

Cancellation is also checked after collecting each returned evaluation, so a rule
that ignores its token cannot turn requested cancellation into completion. An
`OperationCanceledException` without cancellation requested on the supplied token
follows the failure path. Invalid or misattributed evaluation results also fail.
Completed results with error diagnostics still represent completed evaluation;
acceptance or rejection belongs to a separate quality gate.

## Locations, immutability and reproducibility

`SourceLocation` contains a path and one-based line/column coordinates. The start is
inclusive and the end exclusive. Omitting both end coordinates creates a point
location; supplying only one or a reversed range is rejected. A violation or evidence
item may omit its location when no source span is available. Backslashes normalize
to `/`; adapters should supply repository-relative paths and stable symbol/module
identifiers to avoid machine-specific identity differences. Core does no filesystem
resolution, case folding or repository-root discovery.

Contract properties are read-only and input collections/dictionaries are defensively
copied into read-only collections. Evidence and operational issues are ordered by
ordinal canonical content. Findings and diagnostics are ordered by canonical rule ID
and identity keys, with severity as a diagnostic tie-breaker. Identical duplicates
remain present; they share an identity rather than acquiring enumeration-based suffixes.

A diagnostic ID is a lowercase SHA-256 hash of a version-tagged, length-prefixed UTF-8
encoding of the rule ID, affected module/symbol, optional source range, message,
remediation, documentation URL and canonically ordered evidence. Null and empty values
are distinct. Severity, input enumeration order, current culture, timestamps and
process hash seeds do not enter the identity.

The same snapshot, finding content and configuration reproduce the same diagnostics
and identities. Changes to source locations, wording, guidance or evidence may change
an ID. These are snapshot identities. F22 adds separate versioned semantic
fingerprints in `BaselineEntry`, immutable accepted inventories in `BaselineSnapshot`,
and conservative classification in `BaselineComparison`. `GatePolicy.PassesNewFindings`
requires completed coverage and checks only new severities; the original `Passes`
continues checking all findings. See [baselines](baselines.md) for matching and persistence.
