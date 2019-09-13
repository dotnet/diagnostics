``` ini

BenchmarkDotNet=v0.11.5, OS=Windows 10.0.18362
Intel Core i7-6700 CPU 3.40GHz (Skylake), 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=3.0.100-preview8-013656
  [Host] : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 5.0.19.46101, CoreFX 4.700.19.40503), 64bit RyuJIT
  Core   : .NET Core 3.0.0-preview8-28405-07 (CoreCLR 5.0.19.46101, CoreFX 4.700.19.40503), 64bit RyuJIT

Job=Core  Runtime=Core  

```
|              Method |     N |       Mean |      Error |     StdDev | Rank |
|-------------------- |------ |-----------:|-----------:|-----------:|-----:|
|           Gen0Count | 10000 |   4.446 ns |  0.1261 ns |  0.2337 ns |    2 |
|           Gen1Count | 10000 |   4.435 ns |  0.1051 ns |  0.1329 ns |    2 |
|           Gen2Count | 10000 |   4.464 ns |  0.1620 ns |  0.2474 ns |    2 |
|            Gen0Size | 10000 | 162.791 ns |  3.2151 ns |  4.1805 ns |   10 |
|            Gen1Size | 10000 | 161.405 ns |  3.2433 ns |  3.3306 ns |   10 |
|            Gen2Size | 10000 | 162.072 ns |  3.2885 ns |  4.2759 ns |   10 |
|             LOHSize | 10000 | 165.976 ns |  3.3668 ns |  7.7358 ns |   10 |
|            TimeInGC | 10000 |  95.135 ns |  1.9815 ns |  4.2654 ns |    7 |
|       AssemblyCount | 10000 |  95.845 ns |  1.9887 ns |  5.3082 ns |    7 |
|      ExceptionCount | 10000 | 100.712 ns |  2.1900 ns |  6.3884 ns |    8 |
|          Workingset | 10000 | 954.102 ns | 19.0832 ns | 28.5628 ns |   11 |
|     ThreadPoolCount | 10000 |   1.983 ns |  0.0406 ns |  0.0360 ns |    1 |
| LockContentionCount | 10000 |  76.126 ns |  2.1486 ns |  6.2675 ns |    6 |
|    PendingItemCount | 10000 |  10.108 ns |  0.2342 ns |  0.3127 ns |    4 |
|  CompletedItemCount | 10000 |  61.904 ns |  1.3143 ns |  3.0980 ns |    5 |
| TotalAllocatedBytes | 10000 |   4.815 ns |  0.1129 ns |  0.1056 ns |    3 |
|    ActiveTimerCount | 10000 | 144.403 ns |  2.1828 ns |  2.0418 ns |    9 |
