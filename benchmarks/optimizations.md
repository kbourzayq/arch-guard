# Dependency graph and rule allocation improvements

The current [benchmark report](reports/analysis-macos-arm64.md) is an unmodified
BenchmarkDotNet export. Superseded report files are retained in Git history, while
historical measurements stay in this comparison. The first baseline is available
with `git show c091bdd:benchmarks/reports/analysis-macos-arm64.md`; the first
optimized measurement is available with
`git show 9668ae8:benchmarks/reports/analysis-macos-arm64-optimized.md`.

Measurements use Apple M5/macOS 27.0.1, .NET 10.0.13 and SDK 10.0.402. Timing runs
use one launch, three warmups, ten measured iterations and one invocation per
iteration, at normal priority without a profiler or concurrent builds/tests.

## First optimization pass

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

The first pass's final allocation trace contained 7,932 sampled events. String allocation
estimates fell to approximately 378 MiB and character arrays to 11 MiB. The
remaining leading stacks involved evidence-key encoding, Roslyn semantic queries,
repeated type normalization, symbol display and project-path validation.

To reproduce a CPU profile with standard BenchmarkDotNet tooling:

```sh
dotnet run --project benchmarks/ArchitectureGuardian.Benchmarks.csproj --configuration Release --no-restore -- --filter '*Graph*Generated24x40*' '*Rules*Generated24x40*' --profiler EP --artifacts artifacts/performance/profiling
```

Keep profiled measurements separate from the normal benchmark report because the
profiler adds overhead. BenchmarkDotNet also supports the
`EventPipeProfiler(EventPipeProfile.GcVerbose)` attribute for allocation traces.
No profiler dependency or custom measurement loop was added to production code.

## Second optimization pass

A fresh allocation trace after the first pass contained 7,927 samples. Repeated
`NormalizeType` calls rebuilt repository types, validated paths and hashed the
same original definition for many semantic uses. Symbol display and namespace
formatting also repeated pure work. Assembly ownership enumerated every loaded
compilation and allocated a matches array for each lookup.

The extraction context now memoizes normalized original definitions, exact symbol
displays and namespace names. Keys use reference identity: Roslyn semantic
symbol equality can ignore nullable annotations that affect evidence text. Values
stay scoped to the current immutable compiler snapshot and are released with the
extraction. An assembly-identity index retains every matching context, preserving
the source-assembly preference and ambiguous-assembly issue. Null type inputs return
before allocating traversal containers; non-null traversal is unchanged.

Per-rule allocation traces also isolated approximately 32 MiB of sampled AG003
allocation on the generated acyclic input, largely canonicalizing evidence for a
module graph with no findings. Cycle evaluation now builds topology using one
representative per original project pair. Strongly connected components and the
existing shortest, ordinal cycle selection are unchanged. After path selection,
one additional evidence pass serves all cyclic components, collecting every fact
on chosen module hops from all original project pairs and graph levels. Acyclic
graphs avoid that pass and the full evidence snapshots. Unmapped-project issues
still occur once per original pair in the same order. Cancellation checks remain
in evidence scans and graph/path traversal.

The post-change trace contained 5,076 samples. Sampled string allocation fell from
approximately 380 MiB to 257 MiB, string arrays from 110 MiB to 58 MiB, and repeated
type normalization disappeared from the leading stacks. These estimates locate
causes; the native BenchmarkDotNet report supplies comparable operation totals.

### Second-pass measurements

The 29 October run uses the same hardware, SDK, runtime and Monitoring job as the
28 October first-pass measurement. All twelve cases completed successfully. This
table compares the first optimized implementation with the current one; the first
pass's original baseline remains above.

| Input | Method | Mean before → after (ms) | Time change | Allocated before → after (MiB/op) | Allocation change |
| --- | --- | ---: | ---: | ---: | ---: |
| CleanArchitecture | Workspace | 905.431 → 844.108 | -6.8% | 90.5 → 90.5 | 0.0% |
| CleanArchitecture | Graph | 18.722 → 17.205 | -8.1% | 6.0 → 5.4 | -9.6% |
| CleanArchitecture | Rules | 0.985 → 0.915 | -7.1% | 0.3 → 0.3 | -0.7% |
| CleanArchitecture | FullScan | 917.210 → 856.490 | -6.6% | 97.5 → 96.8 | -0.7% |
| Generated24x40 | Workspace | 3,129.324 → 3,179.425 | +1.6% | 617.2 → 615.5 | -0.3% |
| Generated24x40 | Graph | 635.050 → 479.437 | -24.5% | 698.4 → 440.6 | -36.9% |
| Generated24x40 | Rules | 110.259 → 118.997 | +7.9% | 111.3 → 79.7 | -28.4% |
| Generated24x40 | FullScan | 4,007.059 → 3,820.554 | -4.7% | 1,428.1 → 1,139.2 | -20.2% |
| ModularMonolith | Workspace | 853.165 → 874.259 | +2.5% | 90.5 → 90.5 | +0.0% |
| ModularMonolith | Graph | 20.295 → 14.540 | -28.4% | 5.8 → 5.3 | -8.8% |
| ModularMonolith | Rules | 0.936 → 0.978 | +4.4% | 0.3 → 0.3 | -3.3% |
| ModularMonolith | FullScan | 869.860 → 878.766 | +1.0% | 97.3 → 96.8 | -0.5% |

For the generated input, graph allocation falls a further 36.9% (698.4 → 440.6
MiB/op), and mean graph time falls 24.5% (635.050 → 479.437 ms). All-rule allocation
falls 28.4% (111.3 → 79.7 MiB/op). Its mean time increases 7.9% (110.259 → 118.997
ms), while its median falls from 105.184 to 96.775 ms. The new rule measurements
range from 79.165 to 175.329 ms with a 99.9% confidence half-width of 67.454 ms;
the allocation reduction is clear, while all-rule latency remains inconclusive.

Generated full scans allocate 20.2% less (1,428.1 → 1,139.2 MiB/op), with an
observed 4.7% lower mean (4,007.059 → 3,820.554 ms). Confidence intervals overlap,
so the end-to-end timing change is less conclusive than the graph improvement.
Graph Gen0 collections fall from 87 to 55 per operation; rules from 12 to 8; full
scans from 179 to 144. Compared with the original implementation, generated graph
allocation is down 88.7%, rules 81.3% and full scans 77.1%.

Small-input timing changes vary by operation and have short-iteration/outlier
warnings. The modular-monolith rule mean increases 4.4% while its allocations fall
3.3%; its full-scan mean increases 1.0% with overlapping confidence intervals.
Workspace allocation stays essentially flat for all inputs. There is no code
change to workspace loading and no supported workspace performance claim.

## Correctness and remaining bottlenecks

First-pass validation covered 599 tests: 92 compiler, 148 architecture/CLI/MCP and 359
unit/workspace checks. Its original-to-optimized serialized graph and analysis
payloads matched byte for byte for all three inputs. The second pass additionally
compares both reference architectures in Release and AG003 configurations,
including the modular-monolith cycle, plus the generated case. All five complete
graph and analysis payloads are byte-identical before and after, including every
origin, diagnostic ID, evidence field, primary location and structured context.
Inventories stay at 200/715 nodes/edges for Clean Architecture, 199/704 for the
modular monolith (199/711 in AG003), and 1,252/5,892 with all 299 diagnostics for the
generated case.

A new regression exercises two cyclic components, multiple project pairs per
module hop, repeated facts with distinct origins, mixed graph levels and
aggregation. It verifies that all chosen-hop evidence survives and evidence on
other branches is excluded. The existing deep-cycle, deterministic-path, partial
coverage, cancellation, generated/file-local/generic semantic, and changes/full-scan
checks remain covered. All 600 tests pass in the second round: 92 compiler,
148 architecture/CLI/MCP and 360 unit/workspace cases. Locked restores, Release
builds and formatting checks pass for the solution and benchmark project.
Repository architecture validation reports zero findings; report validation
passes 20 serialization samples and four real JSON/SARIF CLI exports.

The main remaining application allocation stacks involve canonical sorting of
retained evidence, source-origin discovery, semantic traversal and project-path
validation. AG004 also repeatedly encodes diagnostic evidence while sorting and
building finding identities. Those are further profiling targets; global caches
would trade allocation traffic for retained memory and complicate equality and
lifetime contracts. Workspace discovery, compilation and validation remain the
main full-scan latency costs: workspace loading averages 3.18 seconds against a
3.82-second generated full scan. Do not add independently measured phase means to
infer a cold scan. The timed Release inputs are acyclic; cyclic behavior has exact
payload and regression-test validation, but cyclic performance has not been
separately quantified. The deferred evidence pass is linear in input evidence
across all selected components, rather than a full pass per cycle.

Rule selection, permissions, severities, diagnostics, evidence, identities and
analysis scope are preserved. Extraction-local accumulation and immutable sharing
keep the existing sequential semantic traversal and rule evaluation. The benchmark
retains its prepared validation context, excludes startup/report rendering, and
measures managed allocation traffic rather than retained/native, child-process or
peak memory. Small iterations still trigger BenchmarkDotNet's minimum-iteration-time
warning. Cross-platform CI remains necessary.
