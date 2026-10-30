# Performance and local telemetry

Understand your own scan before choosing CI timeouts or attributing cost to a rule.
CLI and MCP share the solution-analysis pipeline; compiler-hosted feedback has a
different scope. See [published benchmarks](benchmarks.md) for measured inputs,
results, methodology and limits.

## Opt in to local collection

Set `ARCHGUARD_TELEMETRY` to exactly `1` in the CLI or MCP process environment.
Collection is disabled for every other value and when unset. For the installed CLI:

```sh
ARCHGUARD_TELEMETRY=1 archguard scan \
  Shop.slnx --policy architecture.guardian.yaml --trusted --configuration Release --format json \
  > report.json 2> telemetry.jsonl
```

Replace the input with your restored repository and authorize its build logic as
described in [workspace loading](workspace-loading.md). For PowerShell, set
`$env:ARCHGUARD_TELEMETRY = '1'` before invoking the command and remove it afterward
with `Remove-Item Env:ARCHGUARD_TELEMETRY`. For MCP, add the environment entry to
the client configuration and restart the server. Stdio stdout continues to carry
only MCP messages. Operational errors and existing MCP logs also use stderr;
filter JSON lines by `source == "ArchitectureGuardian.Analysis"` for mixed stderr.
A closed/unwritable local sink drops measurements without changing coverage.

The source/meter name is `ArchitectureGuardian.Analysis`. It exposes:

| Instrument | Unit | Tags |
| --- | --- | --- |
| `archguard.phase.duration` | seconds (`s`) | `phase`, `status`, optional `rule.id` |
| `archguard.rule.evaluations` | evaluations (`{evaluation}`) | `phase=rule`, `status`, `rule.id` |

Phases are `analysis`, `workspace`, `graph`, `rules`, `rule` and `changes`. Durations
are inclusive: `analysis` contains workspace/graph/rules, `rules` contains each
`rule`, and `changes` contains both scans and comparison preparation. Avoid summing
parent and child timings. The counter records each invoked rule once, including
failed/cancelled evaluations; pre-cancelled requests do not count uninvoked rules.
Statuses retain `completed`, `incomplete`, `failed` and `cancelled`. A completed
evaluation may produce findings and fail a severity gate.

Each local JSON line contains `source`, `instrument`, numeric `value`, `unit` and
`tags`. Attributes contain only phase/outcome and rule identity: no repository
paths, source, diagnostic messages or exception details. Metrics never enter
JSON/SARIF reports, baselines or diagnostic fingerprints.

## Interpret the measurements

Compare the same tool/input/policy/configuration/framework context. The workspace
phase includes loading and compilation validation; graph extraction and rule
execution have different costs. Inclusive phases overlap, so summing them double
counts work. Git comparison includes two scans and snapshot preparation.

Record completion status and gate outcome alongside elapsed time. A fast
incomplete result does not prove better performance. Use the [benchmark scope](benchmarks.md#what-the-results-mean-for-adoption)
when comparing allocation traffic with runner memory requirements.

## Privacy and behavior

Local collection writes to stderr and configures no remote exporter. It does not
modify reports, policies, baselines or diagnostic identities, skip rules or relax
coverage. With collection disabled and no external listeners, instrumentation
avoids timing and activity/tag creation. Remove the opt-in environment entry when
finished. Keep stderr logs separate from protocol/report stdout.
