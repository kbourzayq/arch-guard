---
name: architecture-guardian
description: Inspect .NET architecture policy and validate or correct changes with Architecture Guardian MCP tools or CLI. Use when implementing a change under an existing architecture.guardian.yaml or investigating its findings; not as a replacement for normal builds, tests or CI.
---

# Architecture Guardian

Use repository-owned architecture policy to guide a requested .NET change and
verify its dependency boundaries. Architecture constraints belong in
`architecture.guardian.yaml`, not in this skill or copied agent instructions.
The compiler analyzer is complementary feedback; it cannot establish solution coverage.

## Inspect

- Read repository instructions, the YAML policy and the normal build/test/CI commands.
  Discover the intended `.sln`, `.slnx` or C# `.csproj`, explicit policy path,
  configuration and any required target framework. Reuse an established gate's
  context; do not silently select a smaller input or fewer rules to get a pass.
- Locate a configured Architecture Guardian MCP server or installed `archguard`.
  If neither is available, report the setup requirement. Do not invent results or
  install tools/change client configuration without authorization from the task.
- MCP workspace paths are relative to the operator's `Workspace:Root`; CLI paths
  resolve from the command's working directory. Tool arguments cannot grant trust.
  Confirm authorization before restore or workspace loading: repository-controlled
  MSBuild tasks and generators can execute code. If trust is unavailable, inspect
  policy only and report workspace validation as blocked.
- Restore the input explicitly through ordinary command tools in the intended
  configuration/framework when authorized. MCP and full CLI scans do not restore
  it. Re-restore after project/package/build changes. Comparison scans instead
  prepare both snapshots using cache-only restore and require cached dependencies.
- With MCP, call `list_rules` with `policyPath`, then `get_architecture` and
  `analyze_solution` with `inputPath`, `policyPath`, `configuration` and optional
  `targetFramework`. Read ownership, contracts, effective rules and coverage.
- With CLI, run `archguard inspect <input> --policy <policy> --configuration <config>
  --trusted`, then `archguard scan` with the same arguments and `--format json`.
  Add `--framework <tfm>` where required. Use the installed command's full path
  when PATH does not expose it. Save the initial findings and operational issues
  so pre-existing debt remains distinguishable from the requested change.

## Implement

Implement the requested change through the agent's ordinary file and command
tools. Respect resolved ownership, allowed directions and declared public contracts.
Inspect a candidate contract's actual API and permissions before using it; a
suggested contract is not proof that it is a suitable replacement.

Do not weaken policy, suppress diagnostics, remove projects from analysis or refresh
accepted-debt baselines just to obtain a passing result. Policy changes require the
task to authorize an intentional architecture change. If compiler integration is
present, use the same YAML policy with its packaged build metadata targets. During
the XML transition, synchronize any still-configured legacy snapshot with authorized
YAML or assembly-ownership changes.

## Validate and correct

1. Run a fresh full solution scan after editing, keeping the input, policy,
   configuration and framework consistent. Complete each independently required
   build context; one configuration does not prove coverage of another.
2. Interpret completion before compliance. MCP needs `status: "completed"`; inspect
   `gateOutcome` separately. CLI exit `0` is a completed passing gate, `1` is a
   completed failing gate, and `2` leaves compliance unresolved. CLI JSON spells
   an unevaluated gate `notEvaluated`; MCP uses `not-evaluated`. Warnings below the
   threshold can pass. Zero findings cannot establish success without completion.
3. For an actual MCP finding, pass its exact fresh `id` as `diagnosticId` to
   `explain_violation`, with the same input/policy/build context. Inspect evidence,
   dependency path, constraints and remediation. Do not manufacture IDs or
   diagnostic objects. An unknown/stale ID requires a new scan and a current ID.
   CLI fallback uses scan evidence and `archguard explain <rule-id>` for rule
   metadata; CLI `explain` does not resolve diagnostic instances.
4. Correct relevant source/project dependencies, then restore again if build
   inputs changed and rescan. MCP tools are read-only; edits and restore happen
   outside MCP. Address operational issues instead of interpreting partial
   findings as a complete result. Report a blocker if the required context cannot
   complete or the correction exceeds the authorized task.
5. If the repository requires a new-finding gate, use its reviewed baseline or
   explicit Git base in addition to the full-scan result. CLI `--base` and
   `--baseline` are mutually exclusive. MCP `analyze_changes` requires
   `baseReference` and `scope: "solution"`. Base and candidate must both complete;
   a passing comparison can retain existing errors. Do not claim those are fixed.
6. Run the repository's normal build/tests. Keep its independent CLI CI gate;
   agent checks and compiler diagnostics do not replace it.

## Report

State the input, policy and build context; coverage status; gate outcome; relevant
findings corrected and remaining; operational blockers; and build/test results.
Separate full-scan compliance from a new-finding gate and local verification from
hosted CI. Never report approval from an incomplete, failed or cancelled scan.
