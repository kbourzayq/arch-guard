```

BenchmarkDotNet v0.15.8, macOS 27.0.1 (26A434) [Darwin 27.0.0]
Apple M5, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), Arm64 RyuJIT armv8.0-a
  Job-EFGDAT : .NET 10.0.13 (10.0.13, 10.0.1326.50504), Arm64 RyuJIT armv8.0-a

InvocationCount=1  IterationCount=10  LaunchCount=1  
RunStrategy=Monitoring  UnrollFactor=1  WarmupCount=3  

```
| Method    | Input             | Mean         | Error       | StdDev      | Min          | Max          | Median       | Gen0        | Gen1        | Gen2      | Allocated     |
|---------- |------------------ |-------------:|------------:|------------:|-------------:|-------------:|-------------:|------------:|------------:|----------:|--------------:|
| **Workspace** | **CleanArchitecture** |   **823.647 ms** |  **94.6933 ms** |  **62.6337 ms** |   **771.616 ms** |   **925.762 ms** |   **793.587 ms** |  **11000.0000** |   **4000.0000** |         **-** |   **92636.98 KB** |
| Graph     | CleanArchitecture |    31.927 ms |   4.8391 ms |   3.2007 ms |    28.295 ms |    37.429 ms |    31.870 ms |   3000.0000 |   1000.0000 |         - |   25001.55 KB |
| Rules     | CleanArchitecture |     1.105 ms |   0.1095 ms |   0.0724 ms |     1.012 ms |     1.269 ms |     1.091 ms |           - |           - |         - |     376.67 KB |
| FullScan  | CleanArchitecture |   899.887 ms | 116.7467 ms |  77.2207 ms |   822.849 ms | 1,009.888 ms |   852.319 ms |  15000.0000 |   5000.0000 | 1000.0000 |  118666.43 KB |
| **Workspace** | **Generated24x40**    | **3,163.183 ms** | **151.5455 ms** | **100.2379 ms** | **3,021.590 ms** | **3,289.601 ms** | **3,187.060 ms** |  **79000.0000** |  **27000.0000** | **2000.0000** |  **633082.38 KB** |
| Graph     | Generated24x40    | 2,189.364 ms | 190.6885 ms | 126.1286 ms | 2,042.086 ms | 2,414.519 ms | 2,176.686 ms | 487000.0000 | 124000.0000 | 2000.0000 | 4006469.59 KB |
| Rules     | Generated24x40    |   149.614 ms |   4.4681 ms |   2.9554 ms |   146.432 ms |   154.923 ms |   148.532 ms |  50000.0000 |   4000.0000 |         - |  435605.02 KB |
| FullScan  | Generated24x40    | 5,683.783 ms | 333.6155 ms | 220.6660 ms | 5,470.549 ms | 6,263.774 ms | 5,634.323 ms | 619000.0000 | 156000.0000 | 5000.0000 | 5084083.34 KB |
| **Workspace** | **ModularMonolith**   |   **862.161 ms** |  **97.9723 ms** |  **64.8026 ms** |   **795.413 ms** |   **963.122 ms** |   **826.880 ms** |  **11000.0000** |   **4000.0000** |         **-** |   **92973.45 KB** |
| Graph     | ModularMonolith   |    33.585 ms |  13.6574 ms |   9.0335 ms |    25.914 ms |    45.185 ms |    27.961 ms |   2000.0000 |   1000.0000 |         - |   24261.67 KB |
| Rules     | ModularMonolith   |     1.155 ms |   0.1598 ms |   0.1057 ms |     1.002 ms |     1.339 ms |     1.136 ms |           - |           - |         - |     455.23 KB |
| FullScan  | ModularMonolith   |   881.747 ms | 113.6379 ms |  75.1645 ms |   796.871 ms |   986.152 ms |   851.017 ms |  14000.0000 |   4000.0000 |         - |  118069.73 KB |
