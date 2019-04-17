# Trace for performance analysis utility (dotnet-trace)

The dotnet-trace tool is a cross-platform CLI global tool that enables the collection of .NET Core traces of a running process without any native profiler involved. It is built around the EventPipe technology of the .NET Core runtime as an alternative to ETW on Windows and LTTNG on non-Windows.

## Installing dotnet-trace

The first step is to install the dotnet-trace CLI global tool. This requires at least version 2.1 of the SDK to be installed.

```cmd
$ dotnet tool install --global dotnet-trace --version 1.0.3-preview5.19217.3 --add-source https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
You can invoke the tool using the following command: dotnet-trace
Tool 'dotnet-trace' (version '1.0.3-preview5.19217.3') was successfully installed.
```

## Using dotnet-trace

In order to collect traces you will need to:

- First, find out the process identifier (pid) of the .NET Core 3.0 app to collect traces from

  - On Windows, there are options such as using the task manager or the `tasklist` command on the cmd window.
  - On Linux, the trivial option could be using `pidof` on the terminal window.

- Then, run the following command:

```cmd
dotnet-trace collect --process-id <PID> --providers Microsoft-Windows-DotNETRuntime

Press <Enter> to exit...
Connecting to process: <Full-Path-To-Process-Being-Profiled>/dotnet.exe
Collecting to file: <Full-Path-To-Trace>/trace.netperf
  Session Id: <SessionId>
  Recording trace 721.025 (KB)
```

- Finally, stop collection by pressing the \<Enter> key, and *dotnet-trace* will finish logging events to *trace.netperf* file.

## Commonly used keywords for the *Microsoft-Windows-DotNETRuntime* provider

 Runtime keyword name           | Keyword Value     | Description
 ------------------------------ | ----------------: | ------------
None                            |                 0 |
All                             |  FFFFFFFFFFFFFFBF | All does not include start-enumeration.  It just is not that useful.
GC                              |                 1 | Logging when garbage collections and finalization happen.
GCHandle                        |                 2 | Events when GC handles are set or destroyed.
Binder                          |                 4 |
Loader                          |                 8 | Logging when modules actually get loaded and unloaded.
Jit                             |                10 | Logging when Just in time (JIT) compilation occurs.
NGen                            |                20 | Logging when precompiled native (NGEN) images are loaded.
StartEnumeration                |                40 | Indicates that on attach or module load , a rundown of all existing methods should be done.
StopEnumeration                 |                80 | Indicates that on detach or process shutdown, a rundown of all existing methods should be done.
Security                        |               400 | Events associated with validating security restrictions.
AppDomainResourceManagement     |               800 | Events for logging resource consumption on an app-domain level granularity.
JitTracing                      |              1000 | Logging of the internal workings of the Just In Time compiler. This is fairly verbose. It details decisions about interesting optimization (like inlining and tail call).
Interop                         |              2000 | Log information about code thunks that transition between managed and unmanaged code.
Contention                      |              4000 | Log when lock contention occurs. (Monitor.Enters actually blocks).
Exception                       |              8000 | Log exception processing.
Threading                       |             10000 | Log events associated with the threadpool, and other threading events.
JittedMethodILToNativeMap       |             20000 | Dump the native to IL mapping of any method that is JIT compiled. (V4.5 runtimes and above).
OverrideAndSuppressNGenEvents   |             40000 | If enabled will suppress the rundown of NGEN events on V4.0 runtime (has no effect on Pre-V4.0 runtimes).
SupressNGen                     |             40000 | This suppresses NGEN events on V4.0 (where you have NGEN PDBs), but not on V2.0 (which does not know about this bit and also does not have NGEN PDBS).
JITSymbols                      |             60098 | What is needed to get symbols for JIT compiled code.<br>This is equivalent to `Jit+JittedMethodILToNativeMap+Loader+OverrideAndSuppressNGenEvents+StopEnumeration`
Type                            |             80000 | Enables the 'BulkType' event.
GCHeapDump                      |            100000 | Enables the events associated with dumping the GC heap.
GCSampledObjectAllocationHigh   |            200000 | Enables allocation sampling with the 'fast'. Sample to limit to 100 allocations per second per type. This is good for most detailed performance investigations.<br>Note that this DOES update the allocation path to be slower and only works if the process start with this on.
GCHeapSurvivalAndMovement       |            400000 | Enables events associate with object movement or survival with each GC.
GCHeapCollect                   |            800000 | Triggers a GC. Can pass a 64 bit value that will be logged with the GC Start event so you know which GC you actually triggered.
GCHeapAndTypeNames              |           1000000 | Indicates that you want type names looked up and put into the events (not just meta-data tokens).
GCHeapSnapshot                  |           1980001 | This provides the flags commonly needed to take a heap .NET Heap snapshot with ETW.
GCSampledObjectAllocationLow    |           2000000 | Enables allocation sampling with the 'slow' rate, Sample to limit to 5 allocations per second per type. This is reasonable for monitoring. Note that this DOES update the allocation path to be slower and only works if the process start with this on.
GCAllObjectAllocation           |           2200000 | Turns on capturing the stack and type of object allocation made by the .NET Runtime. This is only supported after V4.5.3 (Late 2014) This can be very verbose and you should seriously using GCSampledObjectAllocationHigh instead (and GCSampledObjectAllocationLow for production scenarios).
Stack                           |          40000000 | Also log the stack trace of events for which this is valuable.
ThreadTransfer                  |          80000000 | This allows tracing work item transfer events (thread pool enqueue/dequeue/ioenqueue/iodequeue/a.o.).
Debugger                        |         100000000 | .NET Debugger events
Monitoring                      |         200000000 | Events intended for monitoring on an ongoing basis.
Codesymbols                     |         400000000 | Events that will dump PDBs of dynamically generated assemblies to the ETW stream.
Default                         |         4C14FCCBD | Recommend default flags (good compromise on verbosity).

[source](https://github.com/Microsoft/perfview/blob/master/src/TraceEvent/Parsers/ClrTraceEventParser.cs#L41)

## More information on .NET Providers

 Provider Name                          | Information
 -------------------------------------: | ------------
Microsoft-Windows-DotNETRuntime         | [The Runtime Provider](https://docs.microsoft.com/en-us/dotnet/framework/performance/clr-etw-providers#the-runtime-provider)<br>[CLR Runtime Keywords](https://docs.microsoft.com/en-us/dotnet/framework/performance/clr-etw-keywords-and-levels#runtime)
Microsoft-Windows-DotNETRuntimeRundown  | [The Rundown Provider](https://docs.microsoft.com/en-us/dotnet/framework/performance/clr-etw-providers#the-rundown-provider)<br>[CLR Rundown Keywords](https://docs.microsoft.com/en-us/dotnet/framework/performance/clr-etw-keywords-and-levels#rundown)
Microsoft-DotNETCore-SampleProfiler     | Enable the sample profiler

## *dotnet-trace* help

```cmd
dotnet.exe run -c Release --no-restore --no-build -- collect --help

collect:
  Collects a diagnostic trace from a currently running process

Usage:
  dotnet-trace collect [options]

Options:
  -h, --help
    Shows this help message and exit.

  -p, --process-id <pid>
    The process to collect the trace from

  -o, --output <trace-file-path>
    The output path for the collected trace data. If not specified it defaults to 'trace.netperf'

  --providers <list-of-comma-separated-providers>
    A list of coma separated EventPipe providers to be enabled.
    This option adds to the configuration already provided via the --profile argument. If the same provider if configured in both places, this option takes precedence.
    A provider consists of the name and optionally the keywords, verbosity level, and custom key/value pairs.

    The string is written 'Provider[,Provider]'
        Provider format: (GUID|KnownProviderName)[:Keywords[:Level][:KeyValueArgs]]
            GUID|KnownProviderName  - The provider's name
            Keywords                - 8 character hex number bit mask
            Level                   - A number in the range [0, 5]
                0 - Always
                1 - Critical
                2 - Error
                3 - Warning
                4 - Informational
                5 - Verbose
            KeyValueArgs            - A semicolon separated list of key=value
        KeyValueArgs format: '[key1=value1][;key2=value2]'

    --buffersize <Size>                             Sets the size of the in-memory circular buffer
                                                    in megabytes. Default 256 MB.
```
