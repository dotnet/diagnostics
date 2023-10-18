The newest documentation is now maintained at [dotnet-trace](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-trace.md), [dotnet-counters](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-trace.md), [dotnet-dump](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-dump.md), [dotnet-gcdump](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-gcdump.md), [dotnet-dsrouter](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-dsrouter.md), [dotnet-monitor](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-monitor.md), [dotnet-symbol](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-symbol.md), [dotnet-sos](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-sos.md). This documentation is no longer being updated.

# Installing the diagnostics tools

Depending on the diagnostics scenario you will use one or more of the tools below to get to root cause. By default, these tools are installed to ~/.dotnet/tools. 

### dotnet-counters
In the .NET full/Windows world, we have a myriad of performance counters that can be used to triage and diagnose production issues. For .Net core we have a similar and cross platform story centered around a tool called dotnet-counters. To install the tool, run the following command:

> ```bash
> dotnet tool install --global dotnet-counters
> ```


### dotnet-trace
.NET core includes what is called the 'EventPipe' through which diagnostics data is exposed. The dotnet-trace tool allows you to consume interesting profiling data from your app that can help in scenarios where you need to root cause apps running slow. To install the tool, run the following command:

> ```bash
> dotnet tool install --global dotnet-trace 
> ```


### dotnet-dump
In order to generate core dumps for .net core apps, you can use the dotnet-dump tool. To install the tool, run the following command:

> ```bash
> dotnet tool install --global dotnet-dump
> ```


### dotnet-symbol
In order to debug core dumps, the correct symbols need to be available. The dotnet-symbol tool allows you to point to a core dump and it will automatically download the symbols for you. To install the tool, run:

> ```bash
> dotnet tool install -g dotnet-symbol
> ```

### perfcollect
Thet .NET core runtime is instrumented for both perf and LTTng. To facilitate easier collection of both tracing technologies there is a tool called perfcollect. Perfcollect will output the joint trace data into a nettrace file that can be analyzed using PerfView on Windows. To install the tool run the following commands:

> ```
> curl -OL http://aka.ms/perfcollect
> chmod +x perfcollect
> sudo ./perfcollect install
> ```

