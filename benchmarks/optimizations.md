# Dependency graph allocation improvements

The [original report](reports/analysis-macos-arm64.md) represents source at
`c091bdd`. The [optimized report](reports/analysis-macos-arm64-optimized.md) was
produced with the accompanying implementation changes. Both are unmodified
BenchmarkDotNet Markdown exports from Apple M5/macOS 27.0.1, .NET 10.0.13 and SDK
10.0.402 on 28 October 2026. Both use one launch, three warmups, ten measured
iterations and one invocation per iteration, at normal process priority. The
final timing run had no profiler attached and no concurrent builds or tests.

## Before and after

Times below are means normalized to milliseconds; allocations are cumulative
managed allocation traffic per operation normalized to MiB. Negative changes
indicate reductions. Refer to each native report for dispersion and GC counts.

| Input | Method | Mean before → after (ms) | Time change | Allocated before → after (MiB/op) | Allocation change |
| --- | --- | ---: | ---: | ---: | ---: |
| CleanArchitecture | Workspace | 823.647 → 905.431 | +9.9% | 90.5 → 90.5 | +0.0% |
| CleanArchitecture | Graph | 31.927 → 18.722 | -41.4% | 24.4 → 6.0 | -75.5% |
| CleanArchitecture | Rules | 1.105 → 0.985 | -10.9% | 0.4 → 0.3 | -8.9% |
| CleanArchitecture | FullScan | 899.887 → 917.210 | +1.9% | 115.9 → 97.5 | -15.9% |
| Generated24x40 | Workspace | 3,163.183 → 3,129.324 | -1.1% | 618.2 → 617.2 | -0.2% |
| Generated24x40 | Graph | 2,189.364 → 635.050 | -71.0% | 3,912.6 → 698.4 | -82.1% |
| Generated24x40 | Rules | 149.614 → 110.259 | -26.3% | 425.4 → 111.3 | -73.8% |
| Generated24x40 | FullScan | 5,683.783 → 4,007.059 | -29.5% | 4,964.9 → 1,428.1 | -71.2% |
| ModularMonolith | Workspace | 862.161 → 853.165 | -1.0% | 90.8 → 90.5 | -0.3% |
| ModularMonolith | Graph | 33.585 → 20.295 | -39.6% | 23.7 → 5.8 | -75.3% |
| ModularMonolith | Rules | 1.155 → 0.936 | -19.0% | 0.4 → 0.3 | -23.1% |
| ModularMonolith | FullScan | 881.747 → 869.860 | -1.3% | 115.3 → 97.3 | -15.6% |

The generated case demonstrates the main gains: graph construction uses 82.1%
less allocated memory and 71.0% less mean execution time. Rule evaluation uses
73.8% less allocated memory and 26.3% less mean time. Full scans use 71.2% less
allocated memory and 29.5% less mean time. The graph's Gen0 collection count fell
from 487 to 87 per operation; full scans fell from 619 to 179.

Workspace allocation is essentially unchanged. Small full scans remain dominated
by workspace loading: Clean Architecture's mean increased 1.9%, while the modular
monolith's decreased 1.3%. Their confidence/error ranges overlap, so these runs do
not establish an end-to-end latency improvement for small repositories. The
larger graph/allocation reductions are the supported conclusions, not a claim
that every workload becomes faster.

## Profiling and root causes

BenchmarkDotNet's EventPipe CPU profiler identified extraction, edge freezing,
canonical sorting and identity encoding as expensive application paths. A local
.NET diagnostic probe collected runtime GC allocation ticks around one generated
graph construction and six-rule evaluation, after workspace preparation, using
`Microsoft-Windows-DotNETRuntime` at verbose level with GC keyword `0x1`.
TraceEvent resolved allocation call stacks. The traces, summaries and correctness
snapshots remain ignored under `artifacts/performance/profiling/`.

The original allocation trace contained 42,245 sampled allocation events. Leading
types included approximately 1,916 MiB of strings, 962 MiB of character arrays and
444 MiB of `StringBuilder` objects. Large stacks led from `DependencyGraph` merging
through `DependencyEvidence.SortKey`, `DiagnosticEvidence.CanonicalKey` and
`CanonicalData.Encode`. Extraction also repeatedly constructed and hashed nodes
and singleton edges for observations of the same endpoints. These are sampled
allocation estimates, with a different scope from a complete scan, and do not
represent retained or peak RAM.

Four focused changes address those costs:

1. [Canonical encoding](../src/ArchitectureGuardian.Core/Canonicalization/CanonicalData.cs)
   computes the UTF-16 output length and fills one string. It preserves the exact
   v1 length prefixes, null markers and invariant formatting while eliminating
   builders, numeric prefix strings and concatenated copies of nested values.
2. [Edge construction](../src/ArchitectureGuardian.Core/Dependencies/DependencyEdge.cs)
   deduplicates equal evidence before creating canonical sort keys. All distinct
   origin fields still participate in equality; identical facts from different
   source symbols or bound assemblies remain distinct.
3. [Graph snapshots](../src/ArchitectureGuardian.Core/Dependencies/DependencyGraph.cs)
   reuse an immutable edge when no pair merge is needed. Duplicate pairs still
   merge their complete evidence and all graph collections remain defensive.
4. [Extraction](../src/ArchitectureGuardian.Analysis/Dependencies/DependencyGraphBuilder.cs)
   accumulates a set of origins per endpoint pair and materializes edges once.
   A per-extraction cache reuses nodes with identical kind, identity, name and
   project. Including every endpoint field preserves conflicting-node validation.
   No cache survives a scan or holds data across workspaces.

The final allocation trace contained 7,932 sampled events. String allocation
estimates fell to approximately 378 MiB and character arrays to 11 MiB. The
remaining leading stacks involve evidence-key encoding, Roslyn semantic queries,
repeated type normalization, symbol display and project-path validation.

To reproduce a CPU profile with standard BenchmarkDotNet tooling:

```sh
dotnet run --project benchmarks/ArchitectureGuardian.Benchmarks.csproj --configuration Release --no-restore -- --filter '*Graph*Generated24x40*' '*Rules*Generated24x40*' --profiler EP --artifacts artifacts/performance/profiling
```

Keep profiled measurements separate from the normal benchmark report because the
profiler adds overhead. BenchmarkDotNet also supports the
`EventPipeProfiler(EventPipeProfile.GcVerbose)` attribute for allocation traces.
No profiler dependency or custom measurement loop was added to production code.

## Correctness and remaining bottlenecks

All 599 tests passed: 92 compiler, 148 architecture/CLI/MCP and 359 unit/workspace
checks. This includes deterministic finding identities, generated/file-local and
generic semantic cases, partial coverage, cancellation and changes/full-scan
agreement. A new regression case prevents equal-looking facts from collapsing
when their origin symbol or bound assembly differs. Repository architecture
validation passed with zero findings. Report validation checked 20 serializer
samples and four real JSON/SARIF CLI exports.

Complete serialized graph and analysis payloads from the original and optimized
implementations were byte-identical for all three benchmark inputs, including
every graph origin and diagnostic evidence field. Inventories stayed at 200/715
nodes/edges for Clean Architecture, 199/704 for the modular monolith, and
1,252/5,892 with all 299 diagnostics for the generated case. The fixed v1 diagnostic
identity test also passed. All twelve optimized benchmark cases completed with
completed coverage and iteration validation.

Workspace discovery, compilation and validation now dominate the generated
full-scan latency: workspace loading averages 3.13 seconds against a 4.01-second
full scan. These operations are independently measured and should not be summed
to infer cold startup. Graph extraction still allocates about 698 MiB and rule
evaluation about 111 MiB. Repeated Roslyn type normalization and canonicalization
of retained evidence remain useful future profiling targets. Assembly ownership
lookup also scans loaded compilation contexts; larger project counts need their
own representative measurements before introducing more caches or indexes.

Rule selection, permissions, severities, diagnostics, evidence, identities and
analysis scope are preserved. Extraction-local accumulation and immutable sharing
keep the existing sequential semantic traversal and rule evaluation. The benchmark
retains its prepared validation context, excludes startup/report
rendering, and measures managed allocation traffic rather than retained/native,
child-process or peak memory. Small iterations still trigger BenchmarkDotNet's
minimum-iteration-time warning. Cross-platform CI remains necessary.
