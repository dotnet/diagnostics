# .NET Core Runtime Performance Counter Benchmarks

## Intro 

This part of the repo contains benchmark tests for the internal (or public) APIs that are currently used by CoreCLR to emit these values.

It uses BenchmarkDotNet to measure the raw performance of the underlying APIs to produce runtime counter values. 

An example output looks like this:

```
|              Method |     N |          Mean |       Error |      StdDev | Rank |
|-------------------- |------ |--------------:|------------:|------------:|-----:|
|           Gen0Count | 10000 |      4.199 ns |   0.0302 ns |   0.0283 ns |    3 |
|           Gen1Count | 10000 |      4.181 ns |   0.0392 ns |   0.0367 ns |    3 |
|           Gen2Count | 10000 |      4.239 ns |   0.0237 ns |   0.0222 ns |    3 |
|            Gen0Size | 10000 |    203.872 ns |   1.3477 ns |   1.1254 ns |    8 |
|            Gen1Size | 10000 |    204.422 ns |   1.1772 ns |   1.1011 ns |    8 |
|            Gen2Size | 10000 |    202.772 ns |   1.0590 ns |   0.9906 ns |    8 |
|             LOHSize | 10000 |    202.729 ns |   0.7927 ns |   0.7027 ns |    8 |
|            TimeInGC | 10000 |    133.375 ns |   0.8509 ns |   0.7959 ns |    6 |
|       AssemblyCount | 10000 |    131.736 ns |   0.4501 ns |   0.3990 ns |    6 |
|      ExceptionCount | 10000 |    131.689 ns |   0.7521 ns |   0.7035 ns |    6 |
|          Workingset | 10000 |    684.784 ns |   3.8186 ns |   2.0261 ns |    9 |
|     ThreadPoolCount | 10000 |      1.845 ns |   0.0175 ns |   0.0155 ns |    1 |
| LockContentionCount | 10000 |     66.371 ns |   0.2625 ns |   0.2327 ns |    5 |
|    PendingItemCount | 10000 |     10.218 ns |   0.1101 ns |   0.1030 ns |    4 |
|  CompletedItemCount | 10000 |     66.067 ns |   0.3538 ns |   0.3137 ns |    5 |
| TotalAllocatedBytes | 10000 |      3.636 ns |   0.0185 ns |   0.0173 ns |    2 |
|    ActiveTimerCount | 10000 |    151.222 ns |   2.1967 ns |   2.0548 ns |    7 |
```

## How to run

Simply clone the repo, navigate to src\tests\benchmarks\, and run `RunBenchmarks.cmd` (or `RunBenchmarks.sh`).

