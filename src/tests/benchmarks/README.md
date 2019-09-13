``` .NET Core Runtime Performance Counter Benchmarks ```

## Intro 

This part of the repo contains benchmark tests for the internal (or public) APIs that are currently used by CoreCLR to emit these values.

It uses BenchmarkDotNet to measure the raw performance of the underlying APIs to produce runtime counter values. 

An example output looks like this:

```

```


## How to run

Simply clone the repo, navigate to src\tests\benchmarks\, and run `RunBenchmarks.cmd` (or `RunBenchmarks.sh`).

