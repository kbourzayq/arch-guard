# Performance budgets and local telemetry

These development tools require a checkout containing the F29 implementation;
the original NuGet 0.2.0 release predates them. CLI and MCP share the measured
workspace, graph and rule pipeline. Compiler-hosted analysis is outside these baselines.

## Reproduce BenchmarkDotNet measurements

Install the pinned SDK, Python 3 and the normal development dependencies. From
the repository root:

```sh
dotnet restore ArchitectureGuardian.slnx --locked-mode
python3 eng/benchmark.py --output artifacts/performance/current.json
python3 eng/test-benchmark.py
python3 eng/benchmark.py --check benchmarks/baseline.json --output artifacts/performance/comparison.json
```

The runner builds the non-shipping [BenchmarkDotNet suite](../tests/Fixtures/Performance/AnalysisBenchmarks.cs),
copies the checked-in Clean Architecture and modular-monolith reference solutions,
and generates a deterministic repository with 24 projects/modules in a reference
chain, 40 types per project and eight methods per type (960 source files, 7,680
methods). All six rules are selected. Temporary inputs have no external packages
and restore against cleared package sources. The harness uses centrally pinned
BenchmarkDotNet 0.15.8 and its own committed lock file; shipping packages gain no
benchmark dependency. Input preparation/restore is outside measurements.

BenchmarkDotNet owns process isolation, warmup, timing, statistics and allocation
measurement. The fixed Monitoring job uses one launch, one warmup, five measured
iterations and one invocation per iteration for each of twelve input/method pairs.
Each pair has a separate process; iterations in that process reuse runtime/SDK
initialization. Global setup prepares a workspace and graph, verifies completed
coverage and compares deterministic rule results. Cleanup validates the final
measured results and agreement across benchmark processes, outside timing.

| Method | Measured operation |
| --- | --- |
| `Workspace` | Load and dispose a fresh workspace, including compilation coverage validation |
| `Graph` | Normalize and extract/aggregate a new graph from the prepared workspace |
| `Rules` | Evaluate all six selected rules against the prepared graph |
| `FullScan` | Run the public solution-analysis service, including workspace disposal |

These are warm, prepared-input measurements. They exclude process startup, policy
parsing and report rendering. Graph/rules reuse Roslyn context; do not add their
latencies to workspace time to estimate a cold scan. Change analysis does two full
scans plus snapshot/restore work and is not directly benchmarked here. Short rule
iterations can trigger BenchmarkDotNet's minimum-iteration-time warning; these
initial macrobenchmark budgets do not claim microbenchmark precision.

[`benchmarks/baseline.json`](../benchmarks/baseline.json) contains the full standard
JSON export, including raw iterations, host CPU/OS/runtime/SDK, latency statistics,
MemoryDiagnoser allocation/GC data, input and implementation SHA-256 hashes,
graph sizes, result fingerprints and derived budgets. Detailed execution output
is retained in ignored `artifacts/performance/benchmark.log`. `revision` and `dirty`
describe the measurement checkout; the implementation hash identifies measured
sources even before their commit. Normal telemetry listeners are disabled during
benchmarking. Telemetry itself remains tested independently.

Memory budgets use BenchmarkDotNet's **managed bytes allocated per operation**.
They do not describe live/retained heap size, native allocations, resident process
memory or child MSBuild memory. No custom clocks, allocation counters or memory
samplers participate in the benchmark. See BenchmarkDotNet's
[MemoryDiagnoser documentation](https://benchmarkdotnet.org/articles/configs/diagnosers.html).

Each latency budget is **1.5 × the measured maximum**, rounded up in milliseconds;
each allocation budget is **1.5 × measured allocated bytes**, rounded up. The 50%
allowance is an explicit initial noise margin. `--check` compares new latency
medians and allocated bytes with the fixed budgets and exits one on regression.
Different input/result hashes, missing methods or failed/incomplete benchmarks
stop comparison. Review such changes before measuring a replacement baseline;
do not overwrite it merely to pass a regression. The output cannot overwrite its
comparison baseline. Default output goes under ignored `artifacts/`.

Compare on the same hardware/OS/runtime with other heavy work stopped. These are
initial local investigation thresholds, not universal CI gates or capacity
promises. Re-measure on a controlled runner before enforcing them in CI. The
benchmark dependency and its generated executables remain outside the shipping
solution and are built by the preparation script.

## Initial measured baseline

Measured on Apple M5 (10 cores), macOS 27.0.1, .NET 10.0.13 and SDK 10.0.402.
The OS declined high-priority scheduling; these runs used normal process priority.
The full JSON contains all phase measurements and their individual budgets.

| Input | Projects | Full-scan median | Full-scan budget | Allocated per full scan | Allocation budget |
| --- | ---: | ---: | ---: | ---: | ---: |
| Clean Architecture | 4 | 993 ms | 1,551 ms | 116.5 MiB | 174.8 MiB |
| Modular monolith | 4 | 972 ms | 1,528 ms | 115.9 MiB | 173.9 MiB |
| Generated 24 × 40 | 24 | 5,287 ms | 8,454 ms | 4,962.3 MiB | 7,443.4 MiB |

Allocated bytes are cumulative allocation traffic during one operation; they are
not the memory required to keep that repository in memory. These observations
identify graph extraction in the generated case as a useful optimization target;
this change establishes measurements and does not change analysis algorithms.

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
