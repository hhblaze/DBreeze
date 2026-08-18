# DBreeze .NET 8 TextSearch benchmark comparison

Baseline: commit `3c47ad0a9c7a23663f8b460eee45e166a3dfdfc4` with the same
`TextSearchBenchmarks` workload. Current: working tree after the TextSearch refactoring.

Environment: Windows 11, Intel Core i7-8700, .NET 8.0.30 x64 RyuJIT, BenchmarkDotNet
0.15.8 `ShortRun` (1 launch, 3 warmups, 3 measured iterations), 10,000 indexed
documents. Results are indicative; rerun the default job for release-grade confidence.

| Workload | Baseline mean | Current mean | Time delta | Baseline allocated | Current allocated | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|
| SynchronousIndexing | 101.80 us | 88.79 us | -12.78% | 652.00 KB | 645.56 KB | -0.99% |
| SparseAnd | 193.60 us | 176.41 us | -8.88% | 644.37 KB | 644.42 KB | +0.01% |
| DenseAnd | 3.897 ms | 3.733 ms | -4.20% | 24,095.11 KB | 24,095.19 KB | 0.00% |
| PrefixOr | 7.105 ms | 7.259 ms | +2.16% | 47,406.75 KB | 47,406.62 KB | 0.00% |
| EncryptedSearch | 6.926 ms | 7.095 ms | +2.44% | 47,402.32 KB | 47,401.69 KB | 0.00% |

The geometric-mean speedup across the five workloads is 1.046x. The two small
negative deltas are within the broad confidence intervals of this short run. Query
allocation is dominated by materializing external document IDs and remains effectively
unchanged; the indexing path reduced allocation by about 1%.

Run the workload with:

```powershell
dotnet run --project DBreeze.Net8.Benchmarks -c Release -- --filter "*TextSearchBenchmarks*"
```
