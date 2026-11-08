---
name: architecture-guardian-onboarding
description: Guide first-time .NET architecture discovery, ownership review, proposed YAML validation and incremental adoption with Architecture Guardian CLI or MCP. Use for onboarding or deliberately reviewing an existing policy; use architecture-guardian for ordinary development under approved policy.
---

# Architecture Guardian onboarding

Guide Explore → Understand → Propose → Review → Validate → Adopt. Dependency
extraction and rule evaluation belong to Architecture Guardian's deterministic
engine. Interpret observed evidence and infer intent; never invent dependencies,
ownership approval or compliance. No Architecture Guardian cloud service is needed.

## Explore

Read repository instructions (`AGENTS.md`, `CLAUDE.md`, Copilot instructions as
applicable), solution/project inventory, existing policies and imports, architecture
docs and CI. Record the full intended input, policy root, configurations/frameworks,
selected rules, severity threshold and current adoption mechanism. Ask which input
is intended when several are plausible; do not silently choose the smallest.
Preserve existing source, policies, discovery outputs and baselines.

Find installed `archguard` or configured MCP tools. Inspect `archguard --help`,
`archguard init --help` and MCP tool discovery before using optional capabilities.
The 0.4.0 CLI supports `init --discover`.
Treat installed help as authoritative. There is no MCP discovery/proposal tool in
the current five-tool interface. With MCP only, use manual authoring and the
available validation tools. If neither interface exists, report blocked validation;
do not install/upgrade tools or configure clients without task authorization.

Workspace evaluation and restore can execute repository MSBuild tasks and generators.
Confirm authorization for that build logic before either; read-only MCP is not a
sandbox. CLI uses `--trusted`; MCP trust is operator-owned `Workspace:Trusted=true`
and cannot be granted by tool arguments. Without authorization inspect static
files/policy only, label inventory unevaluated and compliance unresolved.

## Understand and propose

Read [the proposal and validation reference](references/workflow.md) for the
chosen interface and the minimal schema-v1 example. When supported and authorized,
restore the chosen context explicitly, then use deterministic discovery. Preserve
its JSON observations, explored contexts, evidence, uncertainties and compliance
separately from inferred style/module/layer/contract intentions. Discovery exit `0`
means a proposal was produced, even when candidate compliance fails. Incomplete,
failed or cancelled coverage cannot yield a compliance claim.

When discovery is unavailable, say **discovery unavailable** and author a separate
proposed YAML from verified project paths and reviewed intent. Literal XML references
are unevaluated hints until the engine loads the context. Do not create a substitute
dependency engine or a fabricated discovery report. Lack of discovery is not a
reason to replace an existing approved policy with a generic template.

For each proposed module/layer/contract and architectural style, cite repository
paths, declarations, docs or actual engine evidence, distinguish facts from
inferences, and record uncertainty. Names alone do not approve ownership or a
public API. Ask targeted questions such as “Does Shared belong to Billing, or is
it an independently owned contract?” Resolve conflicting ownership before adoption.
Schema v1 owns whole projects; it cannot encode namespace/feature slices. Do not
invent selectors, omit projects or add allow-all permissions to hide ambiguity.

## Review and validate the candidate

Create a review-only candidate at a fresh path under the same policy root, typically
`architecture.guardian.proposed.yaml`. Preserve relative selectors and imported
policy paths. Never overwrite an existing candidate; compare or use a fresh name.
Compare the effective candidate with any approved policy, including imports;
present a reviewable YAML diff and explain changes to ownership, contracts,
permissions, restrictions, rules, severities and gate. Observed edges are not
permission to allow them. Validate/refine using supported `inspect`/MCP tools and
scan the explicit proposed path in each required context before requesting adoption.
Syntax/schema validity alone proves neither resolved ownership nor compliance.

Explain actual findings with their evidence and violated policy:

- **Architectural debt:** a real dependency violates agreed intent. Describe a
  correction and its trade-offs; changing source requires task authorization.
- **Policy mistake:** reviewed intent and selectors/permissions disagree. Propose
  a concrete candidate correction for review, not automatic permission widening.
- **Operational failure:** missing SDK/assets, failed restore, unresolved ownership,
  incomplete/failed/cancelled loading or unavailable history. Repair or report the
  blocker; empty diagnostics do not mean compliance.

Offer adoption choices without silently selecting accepted debt: full enforcement
of all selected findings; an explicitly reviewed CLI baseline of current debt; or
an explicit Git-base new-finding gate. Explain retained debt, context coverage and
limitations of each using the reference. Never silently disable rules, lower
severity, refresh a baseline or call a passing comparison full compliance.

## Adopt and report

An onboarding request permits exploration and a reviewable proposal; **require an
explicit adoption decision before writing the active policy**. Present the exact
candidate/diff, target path, contexts and enforcement choice for that decision.
Authorization to evaluate build logic is separate from adoption approval. If the
user declines or has not approved, leave the approved policy and baselines untouched
and report candidate validation only. If the approved file/candidate changes after
review, present the new diff before applying it. Approval for policy adoption does
not authorize baseline creation, source correction or CI edits unless included.

After approval, use ordinary file tools to create the absent active policy or apply
only the approved reconciliation to an existing policy. Re-run supported inspect
and full scan against the **adopted path** in every selected context; retain issues,
findings and status even when adoption validation fails. Then run any separately
approved baseline/Git-base gate, normal build/tests and independent CI workflow.
Do not roll back or overwrite another edit silently. Do not claim hosted CI passed
from local verification.

End with decisions and their evidence, unresolved questions, exact input and policy,
configuration/framework, selected rules and threshold, interface/tool versions,
coverage status, gate, retained debt, operational issues and limitations. Distinguish
candidate validation from adopted validation, and full enforcement from a
new-finding gate. Use [the report example](references/report-example.md) as a shape,
replacing illustrative values with actual results. Continue ordinary approved-policy
development with `architecture-guardian` if installed; this skill works independently.
