# Performance budgets and local telemetry

These development tools require a checkout containing the F29 implementation;
the original NuGet 0.2.0 release predates them. CLI and MCP share the measured
workspace, graph and rule pipeline. Compiler-hosted analysis is outside these measurements.

## Run the .NET benchmarks

Install the SDK pinned by `global.json`. From the repository root:

```sh
dotnet restore benchmarks/ArchitectureGuardian.Benchmarks.csproj --locked-mode
dotnet run --project benchmarks/ArchitectureGuardian.Benchmarks.csproj --configuration Release --no-restore -- --filter '*'
```

The [benchmark project](../benchmarks/README.md) handles input preparation in C#.
It copies the Clean Architecture and modular-monolith fixtures, generates a
24-project/960-file repository, and restores these package-free inputs outside
measurement. Isolated copies are cleaned up after the run; source fixtures are
unchanged. All six rules are selected.

BenchmarkDotNet 0.15.8 launches a separate process for each input/method pair,
warms up and measures workspace loading, graph extraction, rules and full scans.
The Monitoring job uses three warmups, ten measured iterations and one invocation
per iteration. Results are validated after every iteration, outside timing.
MemoryDiagnoser supplies managed allocations per operation and GC counts. It does
not measure resident memory, retained heap, native allocations or child MSBuild
memory. No custom benchmark timer, allocation counter, sampler, Python wrapper or
JSON baseline is used.

These warm, prepared-input measurements exclude startup, policy parsing and report
rendering. Graph/rules reuse prepared Roslyn context. Short rule iterations can
still have significant relative timing variation; inspect the generated report's
statistics before drawing conclusions. Keep the full semantic, cancellation and
Git comparison tests as correctness checks. Change analysis performs two full
scans plus snapshot/restore work and is not directly benchmarked here.

## Published reports and budgets

The repository includes the [current BenchmarkDotNet Markdown report](../benchmarks/reports/analysis-macos-arm64.md)
and [measured budget table](../benchmarks/README.md#current-measured-budgets).
The [optimization analysis](../benchmarks/optimizations.md) documents the before/after
measurements, allocation profiles, correctness checks and remaining bottlenecks.
BenchmarkDotNet also produces local HTML/CSV reports and detailed logs under
ignored `artifacts/performance/`. The published Markdown is copied from the
standard exporter without editing its numbers or hiding warnings.

For future performance work, rerun the .NET command on comparable hardware and
review the new report against the published measurements and budgets. Initial
thresholds use measured maximum latency or managed allocations plus 50% headroom;
this allowance is an explicit initial noise margin. There is no custom automated
comparison or performance CI gate. Record the environment and review any budget
update alongside the code change; do not silently replace measurements to hide a
regression. Re-measure on a controlled runner before making budgets mandatory in CI.

## Opt in to local collection

Set `ARCHGUARD_TELEMETRY` to exactly `1` in the CLI or MCP process environment.
Collection is disabled for every other value and when unset. For a CLI built from
this checkout:

```sh
ARCHGUARD_TELEMETRY=1 dotnet src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll scan \
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

## Collect through OpenTelemetry

The adapter uses runtime `ActivitySource` and `Meter` APIs, following the
[.NET tracing guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
and [metrics guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation).
It adds no runtime packages or exporter configuration. Embedding hosts can use an
`ActivityListener`/`MeterListener`, or register
`AddSource("ArchitectureGuardian.Analysis")` and
`AddMeter("ArchitectureGuardian.Analysis")` in their own OpenTelemetry SDK providers.
No remote exporter is required or enabled. The environment variable controls the
built-in local listener; external listeners are the embedding host's separate
opt-in. With no listeners, instrumentation skips clocks and activity/tag creation.
Activities use the same phase names/tags and preserve parent spans across awaits.

Instrumentation does not cache or skip rules, change ordering, alter cancellation
tokens or narrow comparison contexts. Tests cover metrics, tracing, outcomes,
cancellation, deterministic reports and instrumented full-scan/Git-comparison
agreement. The normal semantic and comparison suite remains required.
