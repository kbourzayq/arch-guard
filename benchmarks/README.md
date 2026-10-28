# Architecture Guardian benchmarks

This development-only .NET project measures workspace loading, graph extraction,
rule evaluation and full analysis with BenchmarkDotNet 0.15.8. It is outside the
shipping solution and adds no dependency to the CLI, MCP or analyzer packages.

From the repository root, using the SDK pinned by `global.json`:

```sh
dotnet restore benchmarks/ArchitectureGuardian.Benchmarks.csproj --locked-mode
dotnet run --project benchmarks/ArchitectureGuardian.Benchmarks.csproj --configuration Release --no-restore -- --filter '*'
```

The C# entry point copies the two checked-in reference architectures, generates a
24-project repository, and restores all inputs using cleared package sources.
Inputs live in an isolated directory under `artifacts/performance/inputs/` and are
removed when the run ends. Source fixtures are unchanged. Build/restore and input
preparation are outside measurement. No Python runner, custom clocks, allocation
counters, result manifest or JSON baseline is involved.

The generated input is a chain of 24 projects/modules with 40 types per project
and eight methods per type: 960 source files and 7,680 methods. All three inputs
select AG001–AG006. The larger case includes implementation-boundary findings;
completed coverage does not mean that the architecture gate passes.

| Method | Measured operation |
| --- | --- |
| `Workspace` | Load and dispose a fresh workspace, including compilation coverage validation |
| `Graph` | Normalize and extract/aggregate a fresh graph from a prepared workspace |
| `Rules` | Evaluate six selected rules against a prepared graph |
| `FullScan` | Run the public analysis service, including owned workspace disposal |

Each input/method pair runs in its own BenchmarkDotNet process. The fixed
Monitoring job uses three warmups, ten measured iterations and one invocation per
iteration. Global setup prepares Roslyn context and checks deterministic results;
iteration cleanup verifies completed coverage and stable diagnostic identities
and severities after every operation. Graph coverage and ordered node/edge identities
are checked against the prepared graph outside timing. BenchmarkDotNet owns timing,
statistics, managed allocation measurement and GC diagnostics.

These are warm, prepared-input measurements. They exclude process startup, policy
parsing and report rendering. SDK/runtime initialization and Roslyn context are
already available; do not sum independently measured phases to estimate cold
startup. Git comparison's two scans and snapshot/restore work are not directly
benchmarked. The [performance guide](../docs/performance.md) explains runtime
telemetry, measurement scope and limitations.

## Reports and publication

BenchmarkDotNet writes its standard GitHub Markdown, HTML, CSV and detailed log
under ignored `artifacts/performance/`. The entry point returns nonzero if a
benchmark fails or produces no statistics. To list methods without preparing or
restoring inputs:

```sh
dotnet run --project benchmarks/ArchitectureGuardian.Benchmarks.csproj --configuration Release --no-restore -- --list flat
```

The checked-in [measured report](reports/analysis-macos-arm64.md) is an unmodified
copy of BenchmarkDotNet's generated Markdown report. After an intentional new
measurement, copy the generated report for review:

```sh
cp artifacts/performance/results/ArchitectureGuardian.Benchmarks.AnalysisBenchmarks-report-github.md benchmarks/reports/analysis-macos-arm64.md
```

On PowerShell use `Copy-Item` with the same source and destination paths. Use a
separate report filename for another OS/architecture. Publish the Markdown with
the corresponding code change; leave generated HTML, CSV, logs, build outputs and
copied inputs ignored. There is no automatic upload or custom regression gate. CI builds, formats and
discovers this project on Linux, Windows and macOS, without collecting timings.
Compare reports on matching hardware/OS/runtime with heavy background work stopped,
then review performance changes alongside the normal correctness tests.

## Initial measured budgets

Measured on 28 October 2026 with BenchmarkDotNet 0.15.8, Apple M5 (10 cores),
macOS 27.0.1, .NET 10.0.13 and SDK 10.0.402. The OS declined high-priority
scheduling, so measurements used normal process priority. The report includes all
twelve successful cases. Some short iterations triggered minimum-iteration-time
warnings; inspect variation as well as the median before drawing conclusions.

Latency thresholds are 1.5 × the **reported maximum**, rounded up to whole
milliseconds. Allocation thresholds are 1.5 × **reported allocated bytes**, rounded
up to 0.1 MiB. The 50% allowance is an explicit initial noise margin. Values below
are derived from the generated report's displayed precision; there is no saved
JSON baseline or automatic comparison. Review a future latency median or allocation
value above its threshold as a possible regression on comparable hardware.

| Input | Method | Reported max (ms) | Latency budget (ms) | Allocated (MiB/op) | Allocation budget (MiB/op) |
| --- | --- | ---: | ---: | ---: | ---: |
| CleanArchitecture | Workspace | 925.762 | 1,389 | 90.5 | 135.7 |
| CleanArchitecture | Graph | 37.429 | 57 | 24.4 | 36.7 |
| CleanArchitecture | Rules | 1.269 | 2 | 0.4 | 0.6 |
| CleanArchitecture | FullScan | 1,009.888 | 1,515 | 115.9 | 173.9 |
| Generated24x40 | Workspace | 3,289.601 | 4,935 | 618.2 | 927.4 |
| Generated24x40 | Graph | 2,414.519 | 3,622 | 3,912.6 | 5,868.9 |
| Generated24x40 | Rules | 154.923 | 233 | 425.4 | 638.1 |
| Generated24x40 | FullScan | 6,263.774 | 9,396 | 4,964.9 | 7,447.4 |
| ModularMonolith | Workspace | 963.122 | 1,445 | 90.8 | 136.2 |
| ModularMonolith | Graph | 45.185 | 68 | 23.7 | 35.6 |
| ModularMonolith | Rules | 1.339 | 3 | 0.4 | 0.7 |
| ModularMonolith | FullScan | 986.152 | 1,480 | 115.3 | 173.0 |

These are local investigation thresholds, not universal CI or capacity guarantees.
Managed allocations measure allocation traffic during one operation; they do not
measure retained heap, native/child-process memory or peak RAM. BenchmarkDotNet's
Gen0/Gen1/Gen2 columns count collections normalized to 1,000 operations. Each process
retains a prepared validation context, and `FullScan` loads an additional workspace;
retain that setup when comparing reports.
