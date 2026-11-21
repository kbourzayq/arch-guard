```

BenchmarkDotNet v0.15.8, macOS 27.2.1 (26B131) [Darwin 27.2.0]
Apple M5, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), Arm64 RyuJIT armv8.0-a
  Job-EFGDAT : .NET 10.0.13 (10.0.13, 10.0.1326.50504), Arm64 RyuJIT armv8.0-a

InvocationCount=1  IterationCount=10  LaunchCount=1  
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3  

```
| Method       | EvidenceLocations | Mean       | Error     | StdDev    | Min        | Max       | Median     | Ratio | RatioSD | Allocated    | Alloc Ratio |
|------------- |------------------ |-----------:|----------:|----------:|-----------:|----------:|-----------:|------:|--------:|-------------:|------------:|
| **BufferedFile** | **5000**              |   **9.390 ms** |  **1.519 ms** |  **1.005 ms** |   **8.456 ms** |  **11.97 ms** |   **9.234 ms** |  **1.01** |    **0.14** |  **17281.83 KB** |       **1.000** |
| StreamedFile | 5000              |   9.442 ms |  1.656 ms |  1.095 ms |   8.413 ms |  11.61 ms |   9.060 ms |  1.01 |    0.14 |     53.84 KB |       0.003 |
|              |                   |            |           |           |            |           |            |       |         |              |             |
| **BufferedFile** | **200000**            | **205.321 ms** |  **9.636 ms** |  **6.373 ms** | **196.262 ms** | **219.01 ms** | **203.686 ms** |  **1.00** |    **0.04** | **676150.58 KB** |       **1.000** |
| StreamedFile | 200000            | 138.913 ms | 31.990 ms | 21.159 ms | 123.743 ms | 191.34 ms | 130.589 ms |  0.68 |    0.10 |   1479.86 KB |       0.002 |
