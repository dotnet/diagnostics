# Installing the diagnostics tools

Depending on the diagnostics scenario you will use one or more of the tools below to get to root cause. By default, these tools are installed to ~/.dotnet/tools. 

### dotnet-counters
In the .NET full/Windows world, we have a myriad of performance counters that can be used to triage and diagnose production issues. For .Net core we have a similar and cross platform story centered around a tool called dotnet-counters. To install the tool, run the following command:

> ```bash
> dotnet tool install --global dotnet-counters --version 1.0.3-preview5.19228.1 --add-source https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json`
> ```


### dotnet-trace
.NET core includes what is called the 'EventPipe' through which diagnostics data is exposed. The dotnet-trace tool allows you to consume interesting profiling data from your app that can help in scenarios where you need to root cause apps running slow. To install the tool, run the following command:

> ```bash
> dotnet tool install --global dotnet-trace --version 1.0.3-preview5.19228.1 --add-source https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
> ```

Please note, the dotnet-trace tool is version independent but as of right now (preview 5) it requires the 2.1 runtime to be installed to work. This will be fixed moving forward.


### dotnet-dump
In order to generate core dumps for .net core apps, you can use the dotnet-dump tool. To install the tool, run the following command:

> ```bash
> dotnet tool install --global dotnet-dump --version 1.0.3-preview5.19228.1 --add-source https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
> ```

Please note, the dotnet-trace tool is version independent but as of right now (preview 5) it requires the 2.1 runtime to be installed to work. This will be fixed moving forward.


### dotnet-symbol
In order to debug core dumps, the correct symbols need to be available. The dotnet-symbol tool allows you to point to a core dump and it will automatically download the symbols for you. To install the tool, run:

> ```bash
> dotnet tool install -g dotnet-symbol
> ```

### perfcollect
Thet .NET core runtime is instrumented for both perf and LTTng. To facilitate easier collection of both tracing technologies there is a tool called perfcollect. Perfcollect will output the joint trace data into a netperf file that can be analyzed using PerfView on Windows. To install the tool run the following commands:

> ```
> curl -OL http://aka.ms/perfcollect
> chmod +x perfcollect
> sudo ./perfcollect install
> ```

