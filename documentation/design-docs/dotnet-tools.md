# Dotnet Diagnostic Tools CLI Design

## User workflows

These are some quick examples of the work we'd expect a .Net developer to want to get a feel for the design. A more complete reference follows.

### Ad-hoc Health Monitoring / 1st Level Performance Triage

**Seeing performance counter values that refresh periodically in-place**

    > dotnet tool install -g dotnet-counters
    You can invoke the tool using the following command: dotnet-counters
    Tool 'dotnet-counters' (version '1.0.0') was successfully installed.
    > dotnet-counters monitor --process-id 1902 --refresh-interval 1 System.Runtime

    Press p to pause, r to resume, q to quit.
    System.Runtime:
        CPU Usage (%)                                 24
        Working Set (MB)                            1982
        GC Heap Size (MB)                            811
        Gen 0 GC / second                             20
        Gen 1 GC / second                              4
        Gen 1 GC / Second                              1
        Number of Exceptions / sec                     4


### Capture a trace for performance analysis

For analyzing CPU usage, IO, lock contention, allocation rate, etc the investigator wants to capture a performance trace. This trace can then be moved to a developer machine where it can be analyzed with profiling tools such as PerfView/VisualStudio or visualized as a flame graph with speedscope (https://www.speedscope.app/).

**Capture default trace**

    > dotnet tool install -g dotnet-trace
    You can invoke the tool using the following command: dotnet-trace
    Tool 'dotnet-trace' (version '1.0.0') was successfully installed.
    > dotnet trace collect --process-id 1902
    No profile or providers specified, defaulting to trace profile 'cpu-sampling'
    Recording trace 38MB

    's' - stop tracing

...Hit 's'...

    > dotnet trace collect --process-id 1902
    No profile or providers specified, defaulting to trace profile 'cpu-sampling'
    Recording trace 107MB
    Recording complete
    Trace complete: ~/trace.nettrace

**Convert a trace to use with speedscope**

    > dotnet trace convert ~/trace.nettrace --format Speedscope
    Writing:     ~/trace.speedscope.json
    Conversion complete

### Do a (dump-based) memory leak analysis

For analyzing managed memory leaks over time, the investigator first wants to capture a series of dumps that will show the memory growth.

    $ dotnet tool install -g dotnet-dump
    You can invoke the tool using the following command: dotnet-dump
    Tool 'dotnet-dump' (version '1.0.0') was successfully installed.

    $ dotnet dump collect --process-id 1902
    Writing minidump with heap to file ./core_20190226_135837
    Written 98983936 bytes (24166 pages) to core file
    Complete

Some time interval passes

    $ dotnet dump collect --process-id 1902
    Writing minidump with heap to file ./core_20190226_135850
    Written 98959360 bytes (24160 pages) to core file
    Complete

Next the investigator needs to compare the heaps in these two dumps.

    > dotnet dump analyze ./core_20190226_135850
    Loading core dump: ./core_20190226_135850
    $ gcheapdiff ./core_20190226_135837
    Showing top GC heap differences by size
    Type                       Current Heap     Baseline Heap             Delta
                               Size / Count      Size / Count      Size / Count
    System.String           1790650 /  7430   1435870 /  6521   +354780 / + 909
    System.Byte[]             65420 /    26     28432 /     7   + 36988 / +  19
    WebApp1.RequestEntry       1800 /   180      1200 /   120   +   600 / +  60
    ...

    To show all differences use 'gcheapdiff -all ./core_20190226_135850'
    To show objects of a particular type use dumpheap -type <type_name>

    $ dumpheap -type System.String
      Address       MT     Size
     03b51454 725ef698       84
     03b522d4 725ef698       52
     03b52328 725ef698       16
     03b52338 725ef698       28
     32cac458 7214b44c       48
     32cac504 725eeb40       56
     32cac620 725eeb40       94
     32cac6c4 725eeb40       74
     ...

    $ gcroot 03b51454
     Thread 41a0:
         0ad2f274 55f99590 DomainNeutralILStubClass.IL_STUB_PInvoke(System.Windows.Interop.MSG ByRef, System.Runtime.InteropServices.HandleRef, Int32, Int32)
             ebp-c: 0ad2f2b0
                 ->  041095f8 System.Windows.Threading.Dispatcher
                 ...
                 ->  03b512f8 System.AppDomain
                 ->  03b513d0 System.AppDomainSetup
                 ->  03b51454 System.String

     Found 1 unique roots (run 'GCRoot -all' to see all roots).

First we compared the leaky dump to the baseline dump to determine which types were growing, then listed addresses of particular instances of the leaking type, then determined the chain of references that was keeping that instance alive. The investigator may need to sample several instances of the leaked type to identify which ones are expected to be on the heap and which are not.

Note: The dumpheap/gcroot output is identical to SOS. I'm not convinced this output is ideal for clarity, but I am not proposing we change it at this time.

### Install SOS for use with LLDB

    > dotnet tool install -g dotnet-sos
    You can invoke the tool using the following command: dotnet-sos
    Tool 'dotnet-sos' (version '1.0.0') was successfully installed.
    > dotnet sos install
    Installing SOS plugin at ~/.dotnet/sos
    Updating .lldbinit - LLDB will load SOS automatically at startup
    Complete

## Command Line Reference

### dotnet-counters

SYNOPSIS

    dotnet-counters [--version]
                    [-h, --help]
                    <command> [<args>]

OPTIONS

    --version
        Display the version of the dotnet-counters utility.

    -h, --help
        Show command line help

COMMANDS

    list      Display a list of counter names and descriptions
    ps        Display a list of dotnet processes that can be monitored
    monitor   Display periodically refreshing values of selected counters
    collect   Periodically collect selected counter values and export them into a specified file format for post-processing.

LIST

    dotnet-counters list [-h|--help]

    Display a list of counter names and descriptions, grouped by provider.

    -h, --help
        Show command line help

    Examples:
      > dotnet-counters list

      Showing well-known counters only. Specific processes may support additional counters.
      System.Runtime
          total-processor-time           Amount of time the process has utilized the CPU (ms)
          private-memory                 Amount of private virtual memory used by the process (KB)
          working-set                    Amount of working set used by the process (KB)
          virtual-memory                 Amount of virtual memory used by the process (KB)
          gc-total-memory                Amount of committed virtual memory used by the GC (KB)
          exceptions-thrown-rate         Number of exceptions thrown in a recent 1 minute window (exceptions/min)

MONITOR

    Examples:

    1. Monitoring all counters from `System.Runtime` at a refresh interval of 3 seconds:

      > dotnet-counters monitor --process-id 1902 --refresh-interval 3 System.Runtime
    Press p to pause, r to resume, q to quit.
      System.Runtime:
        CPU Usage (%)                                 24
        Working Set (MB)                            1982
        GC Heap Size (MB)                            811
        Gen 0 GC / second                             20
        Gen 1 GC / second                              4
        Gen 1 GC / Second                              1
        Number of Exceptions / sec                     4


    2. Monitoring just CPU usage and GC heap size from `System.Runtime` at a refresh interval of 5 seconds:

      > dotnet-counters monitor --process-id 1902 --refresh-interval 5 System.Runtime[cpu-usage,gc-heap-size]
    Press p to pause, r to resume, q to quit.
      System.Runtime:
        CPU Usage (%)                                 24
        GC Heap Size (MB)                            811

    3. Monitoring EventCounter values from user-defined EventSource: (see https://github.com/dotnet/corefx/blob/main/src/System.Diagnostics.Tracing/documentation/EventCounterTutorial.md on how to do this.0)

      > dotnet-counters monitor --processId 1902 Samples-EventCounterDemos-Minimal

    Press p to pause, r to resume, q to quit.
        request                                      100

    Syntax:

    dotnet-counters monitor [-h||--help]
                            [-p|--process-id <pid>]
                            [--refresh-interval <sec>]
                            counter_list

    Display periodically refreshing values of selected counters

    -h, --help
        Show command line help

    -p,--process-id
        The ID of the process that will be monitored

    --refresh-interval
        The number of seconds to delay between updating the displayed counters

    counter_list
        A space separated list of counters. Counters can be specified provider_name[:counter_name]. If the
        provider_name is used without a qualifying counter_name then all counters will be shown. To discover
        provider and counter names, use the list command.


COLLECT


    Examples:

    1. Collect the runtime performance counters at a refresh interval of 10 seconds and export it as a JSON file named "test.json".
```
    dotnet run collect --process-id 863148 --refresh-interval 10 --output test --format json
```

    2. Collect the runtime performance counters as well as the ASP.NET hosting performance counters at the default refresh interval (1 second) and export it as a CSV file named "mycounter.csv". 
```
    dotnet run collect --process-id 863148 --output mycounter --format csv System.Runtime Microsoft.AspNetCore.Hosting
```


    Syntax:

```
    dotnet-counters collect [-h||--help]
                            [-p|--process-id <pid>]
                            [-o|--output <name>]
                            [--format <csv|json>]
                            [--refreshInterval <sec>]
                            counter_list
    
    Periodically collect selected counter values and export them into a specified file format for post-processing.
    
    -h, --help
        Show command line help
    
    -p,--process-id
        The ID of the process that will be monitored

    -o, --output
        The name of the output file

    --format
        The format to be exported. Currently available: csv, json

    --refresh-interval
        The number of seconds to delay between updating the displayed counters
    
    counter_list
        A space separated list of counters. Counters can be specified provider_name[:counter_name]. If the
        provider_name is used without a qualifying counter_name then all counters will be shown. To discover
        provider and counter names, use the list command.

```


### dotnet-trace

SYNOPSIS

    dotnet-trace [options] [command] [<args>]

OPTIONS

    --version
        Display the version of the dotnet-trace utility.

    -h, --help
        Show command line help

COMMANDS

    collect         Collects a diagnostic trace from a currently running process
    ps              Lists dotnet processes that can be attached to.
    list-profiles   Lists pre-built tracing profiles with a description of what providers and filters are in each profile.
    convert         Converts traces to alternate formats for use with alternate trace analysis tools

COLLECT

    dotnet-trace collect -p|--process-id <pid>
                         [-h|--help]
                         [-o|--output <trace-file-path>]
                         [--profile <profile_name>]
                         [--providers <list-of-comma-separated-providers>]
                         [--format <trace-file-format>]

    Collects a diagnostic trace from a currently running process or launch a child process and trace it. Append -- to the collect command to instruct the tool to run a command and trace it immediately.

    -p, --process-id
        The process to collect the trace from

    -h, --help
        Show command line help

    -o, --output
        The output path for the collected trace data. If not specified it defaults to ./trace.nettrace

    --profile
        A named pre-defined set of provider configurations that allows common tracing scenarios to be specified
        succinctly. The options are:
        cpu-sampling    Useful for tracking CPU usage and general .NET runtime information. This is the default 
                        option if no profile or providers are specified.
        gc-verbose      Tracks GC collection and sampled object allocations
        gc-collect      Tracks GC collection only at very low overhead

    --providers
        A list of comma separated EventPipe providers to be enabled.
        These providers are in addition to any providers implied by the --profile argument. If there is any
        discrepancy for a particular provider, the configuration here takes precedence over the implicit
        configuration from the profile.
        A provider consists of the name and optionally the keywords, verbosity level, and custom key/value pairs.

        The string is written 'Provider[,Provider]'
            Provider format: KnownProviderName[:Keywords[:Level][:KeyValueArgs]]
                KnownProviderName       - The provider's name
                Keywords                - 8 character hex number bit mask
                Level                   - A number in the range [0, 5], or their corresponding text values (refer to https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventlevel?view=netframework-4.8).
                KeyValueArgs            - A semicolon separated list of key=value
            KeyValueArgs format: '[key1=value1][;key2=value2]'

    --buffersize <Size>
        Sets the size of the in-memory circular buffer in megabytes. Default 256 MB.

    --format
        The format of the output trace file. The default value is nettrace.

    --show-child-io
        Shows the input and output streams of a launched child process in the current console.

    Examples:
      
      To perform a default `cpu-tracing` profiling:

      > dotnet trace collect --process-id 1902
      No profile or providers specified, defaulting to trace profile 'cpu-sampling'
      Recording trace 38MB

      's' - stop tracing


      To collect just the GC keyword events from the .NET runtime at informational level:

      > dotnet trace collect --process-id 1902 --providers Microsoft-Windows-DotNETRuntime:0x1:Informational



CONVERT

    dotnet-trace convert [-h|--help]
                         [-o|--output <output_file_path>]
                         --format <format>
                         <trace_file_path>

    Converts traces to alternate formats for use with alternate trace analysis tools

    -h, --help
        Show command line help

    -o, --output
        The path where the converted file is written. If unspecified the file is written in the current directory
        using the same base filename as the input file and the extension appropriate for the new format.

    --format
        Specifies the format to convert the nettrace file to. Currently, the only valid input is 'speedscope'.

    trace_file_path
        The path to the trace file that should be converted. The trace file can be a nettrace file. Defaults to 'trace.nettrace'.

    Examples:
      > dotnet-trace convert trace.nettrace -f speedscope
      Writing:       ./trace.speedscope.json
      Conversion complete

### dotnet-stack

SYNOPSIS

    dotnet-stack [options] [command] [<args>]

OPTIONS

    --version
        Display the version of the dotnet-trace utility.

    -h, --help
        Show command line help

COMMANDS

    report         Displays stack traces for the target process

REPORT

    dotnet-stack report -p|--process-id <pid>
                        -n|--name <process-name>
                        [-h|--help]

    Prints the managed stack from every thread in the target process

    -h, --help
        Show command line help

    Examples:
      > dotnet-stack report -p 1234
      Thread (0x151c):
          [Native Frames]
          System.Private.CoreLib!System.Threading.ManualResetEventSlim.Wait(int, System.Threading.CancellationToken)
          System.Private.CoreLib!System.Threading.Tasks.Task.SpinThenBlockingWait(int, System.Threading.CancellationToken)
          System.Private.CoreLib!System.Threading.Tasks.Task.InternalWaitCore(int, System.Threading.CancellationToken)
          System.Private.CoreLib!System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task)
          System.Private.CoreLib!System.Runtime.CompilerServices.TaskAwaiter.GetResult()
          Microsoft.Extensions.Hosting.Abstractions!Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run(Microsoft.Extensions.Hosting.IHost)
          testtesttest!testtesttest.Program.Main(System.String[])

      Thread (0x152b):
          [Native Frames]
          System.IO.FileSystem.Watcher!System.IO.FileSystemWatcher.RunningInstance.StaticWatcherRunLoopManager.WatchForFileSystemEventsThreadStart(System.Threading.ManualResetEventSlim, Microsoft.Win32.SafeHandles.SafeEventStreamHandle)
          System.IO.FileSystem.Watcher!System.IO.FileSystemWatcher.RunningInstance.StaticWatcherRunLoopManager.<>c.<ScheduleEventStream>(System.Object)
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart_Context(System.Object)
          System.Private.CoreLib!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object)
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart(System.Object)

      Thread (0x153a):
          [Native Frames]
          System.Private.CoreLib!System.Threading.SemaphoreSlim.WaitUntilCountOrTimeout(int, uint, System.Threading.CancellationToken)
          System.Private.CoreLib!System.Threading.SemaphoreSlim.Wait(int, System.Threading.CancellationToken)
          System.Collections.Concurrent!System.Collections.Concurrent.BlockingCollection<Microsoft.Extensions.Logging.Console.LogMessageEntry>.TryTakeWithNoTimeValidation(int, System.Threading.CancellationToken, System.Threading.CancellationTokenSource)
          System.Collections.Concurrent!System.Collections.Concurrent.BlockingCollection<Microsoft.Extensions.Logging.Console.LogMessageEntry>.GetConsumingEnumerable().MoveNext()
          Microsoft.Extensions.Logging.Console!Microsoft.Extensions.Logging.Console.ConsoleLoggerProcessor.ProcessLogQueue()
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart_Context(System.Object)
          System.Private.CoreLib!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object)
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart()

      Thread (0x4125):
          [Native Frames]
          System.Private.CoreLib!System.Threading.Thread.Sleep(System.TimeSpan)
          Microsoft.AspNetCore.Server.Kestrel.Core!Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure.Heartbeat.TimerLoop()
          Microsoft.AspNetCore.Server.Kestrel.Core!Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure.Heartbeat.ctor(System.Object)
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart_Context(System.Object)
          System.Private.CoreLib!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object)
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart(System.Object)

      Thread (0x5hf3):
          [Native Frames]
          System.Net.Sockets!System.Net.Sockets.SocketAsyncEngine.EventLoop()
          System.Net.Sockets!System.Net.Sockets.SocketAsyncEngine.ctor( System.Object)
          System.Private.CoreLib!System.Threading.ThreadHelper.ThreadStart(System.Object)

### dotnet-dump

SYNOPSIS

    dotnet-dump [--version]
                [-h, --help]
                <command> [<args>]

OPTIONS

    --version
        Display the version of the dotnet-dump utility.

    -h, --help
        Show command line help

COMMANDS

    collect   Capture dumps from a process
    analyze   Starts an interactive shell with debugging commands to explore a dump
    ps        Display a list of dotnet processes to create dump from

COLLECT

    dotnet-dump collect -p|--process-id <pid> [-h|--help] [-o|--output <output_dump_path>] [--type <dump_type>]

    Capture dumps (core files on Mac/Linux) from a process

    Usage:
      dotnet-dump collect [options]

    Options:
      -p, --process-id
          The process to collect a memory dump from.

      -h, --help
          Show command line help

      -o, --output
          The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and
          './core_YYYYMMDD_HHMMSS' on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full
          path and file name of the dump.

      --type
          The dump type determines the kinds of information that are collected from the process. There are two types:
          heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks,
                 exception information, handle information, and all memory except for mapped images.
          mini - A small dump containing module lists, thread lists, exception information and all stacks.

          If not specified 'heap' is the default.

Examples:

    $ dotnet dump collect --process-id 1902 --type mini
    Writing minidump to file ./core_20190226_135837
    Written 98983936 bytes (24166 pages) to core file
    Complete

    $ dotnet dump collect --process-id 1902 --type mini
    Writing minidump to file ./core_20190226_135850
    Written 98959360 bytes (24160 pages) to core file
    Complete

ANALYZE

    dotnet-dump analyze [-h|--help] [-c|--command <command>] dump_path

    Starts an interactive shell with debugging commands to explore a dump

    Usage:
      dotnet-dump analyze [options] <dump_path>

    Arguments:
      <dump_path>    Name of the dump file to analyze.

    Options:
      -h, --help
          Show command line help

      -c, --command <command>
          Run the command on start.

Examples:

      $ dotnet-dump analyze ./core_20190226_135850
      Loading core dump: ./core_20190226_135850
      Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.
      Type 'quit' or 'exit' to exit the session.
      >
      ... use the nested command-line. The commands are broken out in the following section

### dotnet-dump analyze nested command syntax

The following commands are supported:

```
   exit, quit                           Exit interactive mode.
   help <command>                       Display help for a command.
   lm, modules                          Displays the native modules in the process.
   threads, setthread <threadid>        Sets or displays the current thread id for the SOS commands.
   clrstack <arguments>                 Provides a stack trace of managed code only.
   clrthreads <arguments>               List the managed threads running.
   dumpasync <arguments>                Displays info about async state machines on the garbage-collected heap.
   dumpassembly <arguments>             Displays details about an assembly.
   dumpclass <arguments>                Displays information about a EE class structure at the specified address.
   dumpdelegate <arguments>             Displays information about a delegate.
   dumpdomain <arguments>               Displays information all the AppDomains and all assemblies within the domains.
   dumpheap <arguments>                 Displays info about the garbage-collected heap and collection statistics about objects.
   dumpil <arguments>                   Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.
   dumplog <arguments>                  Writes the contents of an in-memory stress log to the specified file.
   dumpmd <arguments>                   Displays information about a MethodDesc structure at the specified address.
   dumpmodule <arguments>               Displays information about a EE module structure at the specified address.
   dumpmt <arguments>                   Displays information about a method table at the specified address.
   dumpobj <arguments>                  Displays info about an object at the specified address.
   dso, dumpstackobjects <arguments>    Displays all managed objects found within the bounds of the current stack.
   eeheap <arguments>                   Displays info about process memory consumed by internal runtime data structures.
   finalizequeue <arguments>            Displays all objects registered for finalization.
   gcroot <arguments>                   Displays info about references (or roots) to an object at the specified address.
   gcwhere <arguments>                  Displays the location in the GC heap of the argument passed in.
   ip2md <arguments>                    Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.
   name2ee <arguments>                  Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.
   pe, printexception <arguments>       Displays and formats fields of any object derived from the Exception class at the specified address.
   syncblk <arguments>                  Displays the SyncBlock holder info.
   histclear <arguments>                Releases any resources used by the family of Hist commands.
   histinit <arguments>                 Initializes the SOS structures from the stress log saved in the debuggee.
   histobj <arguments>                  Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.
   histobjfind <arguments>              Displays all the log entries that reference an object at the specified address.
   histroot <arguments>                 Displays information related to both promotions and relocations of the specified root.
   setsymbolserver <arguments>          Enables the symbol server support.
```

The "modules", "threads" and "setthread" commands display/control the native state.

In addition new commands are listed below:

GCHEAPDIFF

    gcheapdiff <path_to_baseline_dump>

    Compares the current GC heap to the one contained in the baseline dump

    path_to_baseline_dump
        The path to another dump that contains the baseline

    Examples:
      $ gcheapdiff ./core_20190226_135837
      Showing top GC heap differences by size

      Type                       Current Heap     Baseline Heap             Delta
                                 Size / Count      Size / Count      Size / Count
      System.String           1790650 /  7430   1435870 /  6521   +354780 / + 909
      System.Byte[]             65420 /    26     28432 /     7   + 36988 / +  19
      WebApp1.RequestEntry       1800 /   180      1200 /   120   +   600 / +  60
      ...

      To show all differences use 'gcheapdiff -all ./core_20190226_135837'
      To show objects of a particular type use DumpHeap -type <type_name>

### dotnet-sos

SYNOPSIS

    dotnet-sos [--version]
               [-h, --help]
               <command> [<args>]

OPTIONS

    --version
        Display the version of the dotnet-dump utility.

    -h, --help
        Show command line help

COMMANDS

    install    Installs SOS and configures LLDB to load it on startup
    uninstall  Uninstalls SOS and reverts any configuration changes to LLDB

INSTALL

    dotnet-sos install [-h|--help]
                       [--verbose]

    Installs SOS and configures LLDB to load it on startup

    -h, --help
        Show command line help

    --verbose
        Enables verbose logging

    Examples:
      >dotnet-sos install
      Installing SOS plugin at ~/.dotnet/sos
      Updating .lldbinit - LLDB will load SOS automatically at startup
      Complete

UNINSTALL

    dotnet-sos uninstall [-h|--help]
                         [--verbose]

    Uninstalls SOS and reverts any configuration changes to LLDB

    -h, --help
        Show command line help

    --verbose
        Enables verbose logging

    Examples:
      >dotnet-sos uninstall
      Reverting .lldbinit - LLDB will no longer load SOS at startup
      Uninstalling SOS from ~/.dotnet/sos
      Complete

### dotnet-gcdump

SYNOPSIS

    dotnet-gcdump [--version]
                  [-h, --help]
                  <command> [<args>]

OPTIONS

    --version
        Display the version of the dotnet-gcdump utility.

    -h, --help
        Show command line help

COMMANDS

    collect   Capture dumps from a process
    report    Generate report into stdout from a previously generated gcdump or from a running process.

COLLECT

    dotnet-gcdump collect -p|--process-id <pid> [-h|--help] [-o|--output <output_dump_path>] [-v|--verbose]

    Capture GC dumps from a dotnet process

    Usage:
      dotnet-gcdump collect [options]

    Options:
      -p, --process-id
          The process to collect a gc dump from.

      -h, --help
          Show command line help

      -o, --output
          The path where collected gcdumps should be written. Defaults to '.\YYYYMMDD_HHMMSS_<pid>.gcdump' where YYYYMMDD is Year/Month/Day
          and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path and file name of the dump.
      
      -v, --verbose
          Turns on logging for gcdump

Examples:

    $ dotnet gcdump collect --process-id 1902
    Writing gcdump to file ./20190226_135837_1902.gcdump
    Wrote 12576 bytes to file
    Complete

REPORT

    dotnet-gcdump report <gcdump_filename>
    
    Generate report into stdout from a previously generated gcdump or from a running process.
    
    Usage:
      dotnet-gcdump report [options] [<gcdump_filename>]

    Arguments:
      <gcdump_filename>  The file to read gcdump from.
  
    Options:
      -p, --process-id   The process id to collect the trace.
      -t, --report-type  The type of report to generate. Available options: heapstat (default)

Examples:

    $ dotnet gcdump report 20200207_094403_19847.gcdump
      4,786,378  GC Heap bytes
         63,201  GC Heap objects

    Object Bytes     Count  Type
         131,096         1  System.Byte[] (Bytes > 100K)  [System.Private.CoreLib.dll]
          57,756         1  System.String (Bytes > 10K)  [System.Private.CoreLib.dll]
          31,128         1  System.Int32[] (Bytes > 10K)  [System.Private.CoreLib.dll]
          28,605         5  System.Byte[] (Bytes > 10K)  [System.Private.CoreLib.dll]
          22,432         9  System.Object[] (Bytes > 10K)  [System.Private.CoreLib.dll]
    ...

    $ dotnet gcdump report -p 1752 | head -9
      1,302,804  GC Heap bytes
         16,211  GC Heap objects
         27,858  Total references

    Object Bytes     Count  Type
          31,128         1  System.Int32[] (Bytes > 10K)  [System.Private.CoreLib.dll]
          24,468         1  System.String (Bytes > 10K)  [System.Private.CoreLib.dll]
          12,800         3  System.Object[] (Bytes > 10K)  [System.Private.CoreLib.dll]
           7,904         1  Entry<System.String,System.Drawing.Color>[] (Bytes > 1K)  [System.Private.CoreLib.dll]
           7,074         4  System.String (Bytes > 1K)  [System.Private.CoreLib.dll]
    ...

## Future suggestions

Work described in here captures potential future directions these tools could take given time and customer interest. Some of these might come relatively soon, others feel quite speculative or duplicative with existing technology. Regardless, understanding potential future options helps to ensure that we don't unknowingly paint ourselves into a corner or build an incoherent offering.

### dotnet-counters

- Add a profile option similar to dotnet-trace that allows curated sets of counters to be shown that could span multiple providers.

- Dynamic counter enumeration

Add a --process-id to the list command in order to dynamically determine a full set of available counters rather than just well-known counters

    > dotnet-counters list --process-id 1902
    Json-DotNET-Events
        parse-count                    The number of times you called Json.Parse
    Microsoft-Windows-DotNETRuntime
        total-processor-time           Amount of time the process has utilized the CPU (s)
        private-memory                 Amount of private virtual memory used by the process (MB)
        working-set                    Amount of working set used by the process (MB)
        virtual-memory                 Amount of virtual memory used by the process (MB)
        gc-total-memory                Amount of committed virtual memory used by the GC (MB)
        exceptions-thrown-rate         Number of exceptions thrown in a recent 1 minute window (exceptions/min)
        lock-contention-rate           Number of instances of lock contention on runtime implemented locks in a
                                       recent 1 minute window (contentions/min)
    Microsoft-AspNet
        request-rate                   Number of requests handled in a recent one second interval (requests/sec)
        request-latency                Time to respond to a request, averaged over all requests in a recent                         one second interval (ms)
    MyAppEvents
        Main-Page-Hits                 # of requests for our main page
        user-sessions                  # of users logged in right now

- View command

Dumps a snapshot of counters on demand. In order to make this command fast the EventCounter infrastructure would need to support a command that dumps the counters immediately rather than waiting for their next transmission interval.

    dotnet-counters view [-h||--help]
                         [-p|--process-id <pid>]
                         counter_list

    Display current values of selected counters

    -h, --help
        Show command line help

    -p,--process-id
        The process to display counters for

    counter_list
        A space separated list of counters. Counters can be specified provider_name[:counter_name]. If the
        provider name is used without a qualifying counter name then all counters for that provider will be shown.
        To discover provider and counter names, use the list command.


    Examples:
    > dotnet counters view --processId 1902 Microsoft-Windows-DotNETRuntime Microsoft-AspNet
      Microsoft-Windows-DotNETRuntime:
          Total Processor Time (ms)              173923.48
          Private Virtual Memory (MB)                 1094
          Working Set (MB)                            1982
          Virtual Memory (MB)                         3041
          GC Heap Memory (MB)                          784
          Exception Thrown Rate (exceptions/min)       117
          Lock Contention Rate (contentions/min)      1792

       Microsoft-AspNet:
          Request Rate (requests/sec)                 1915
          Request Latency (ms)                          34

### dotnet-trace

- Capture GC heap snapshot

Add a command to `dotnet-trace collect` that enables the collection of GC heap snapshots on an active tracing session.

- Compress a trace and any necessary symbols into a single zip file for easy off-machine analysis

    OPTION

        [--pack]  Automatically runs the pack command after collection is complete. Use dotnet-trace pack --help for more details.

    USAGE

        > dotnet trace collect --process-id 1902 --pack
        Recording trace 107MB
        Recording complete (process exited)
        Packing...
        Trace complete: ~/trace.nettrace.zip

    VERB

        pack      Compresses a trace and any necessary symbols into a single zip file for easy off-machine analysis

    PACK

        dotnet-trace pack [-h|--help]
                        [-o|--output <output_file_path>]
                        [--verbose]
                        <trace_file_path>

        Compresses a trace and any necessary symbols into a single zip file for easy off-machine analysis

        -h, --help
            Show command line help

        -o, --output
            The path where the pack is written. If unspecified the pack is written in the current directory
            using the same base filename as the input file and the .zip extension.

        --verbose
            Logs detailed information about what the pack command is doing.

        trace_file_path
            The path to the trace file that should be packed.


        Examples:
        > dotnet-trace pack trace.nettrace
        Packing:      ./trace.nettrace.zip
        Pack complete

        > dotnet-trace pack --verbose trace.nettrace
        Packing:      /usr/home/noahfalk/trace.nettrace.zip
        Compressing   /usr/home/noahfalk/trace.nettrace
        Checking      /usr/bin/dotnet/shared/3.0.170/System.Private.CoreLib.dll
            Not packing symbols - Policy skips Microsoft binary
        Checking      /usr/bin/dotnet/shared/3.0.170/System.Diagnostics.dll
            Not packing symbols - Policy skips Microsoft binary
        Checking      /usr/home/noahfalk/MyApp/Newtonsoft.Json.dll
            Searching for Newtonsoft.Json.pdb
            Searching   /usr/home/noahfalk/MyApp/Newtonsoft.Json.pdb
            Not packing symbols - Newtonsoft.Json.pdb not found
        Checking      /usr/home/noahfalk/MyApp/MyApp.dll
            Searching for MyApp.pdb
            Searching   /usr/home/noahfalk/MyApp/MyApp.pdb
            Found matching symbol file
            Compressing symbol file /usr/home/noahfalk/MyApp/MyApp.pdb
        ...
        Pack Complete

- Multi-process collection

Make the --process-id argument optional or let it take a list of ids to create multi-process traces

- Collection triggers

Add additional arguments to the collect command to support conditions for starting, stopping, or adjusting the trace capture. This configuration could also be added to the profiles.

- Custom profiles

Add a serialization format for profiles that lets users author new ones and specify them to collect on the command line.

- Provider/event enumeration

Add a command that interrogates a running process (or maybe binary?) to extract a set of providers and events it supports emitting.

- Monitor command

Add a command similar to collect, except it prints event data to the console as a real time log

- Report command

Add a command that produces various static reports from the trace data, either in text or html. Some of the reports might leverage existing PerfView reporting work. Others might be reports such as functions that use the most CPU, IO, or memory allocation.

- Run command

Add a run command that executes a specific process and captures a trace of it

- Start / Stop / Cancel / Marker / UserCommand commands

Add support for a persistent session that can be interacted with. Collect/Run/Monitor act as a shortcut to start session, attach/launch process, then stop session all in one command. Monitor could also gain an argument to eavesdrop an existing session rather than create a new one.

- Web-Monitor command

Add a command that launches a web-service which displays the streaming trace data. Further work might allow for real-time analysis.

- Support for ETW / LTTNG / other tracing systems from this tool

We could interoperate in various ways such as:

1. Converting between serialized formats
2. Allowing trace consuming commands (report/monitor) to consume trace data from external trace files/trace sessions
3. Allowing the session control/collection commands to control alternate session types

### dotnet-dump

- Packing

Allow gathering of symbols and binaries, similar to trace packing, that lets us more easily do cross-machine investigation.

- Triggers

We could allow dumps to be collected in response to various process or machine-state conditions such as exceptions being thrown, or performance metrics

- Extensible commands

We could allow users to create their own commands packaged into a managed assembly and loaded as plugins, similar to how many command-line debuggers allow plugins. We would need to define an extensibility interface, but the initial thought would be to allow access to console output + the CLRMD API for inspecting dump contents.

### dotnet-sos

- Allow a custom installation path

### dotnet-ps

Add a new tool that lists a snapshot of processes with .Net Core loaded. It should be similar to the linux tool ps but it needs to show the managed entrypoint and entrypoint assembly. Right now all .Net Core apps tend to be indistinguishable 'dotnet.exe' processes.

### dotnet-stack

Show a snapshot of all a processes threads with a callstack for each.


## General CLI/Workflow Questions

1. Do we want an alternate installation technique that doesn't require the SDK?

    Not immediately, but it should probably follow shortly after getting an SDK based option in place. There are several alternate installation options customers might want for more production oriented scenarios such as FDD and self-contained apps available as network downloads, or docker images that are pre-provisioned with the tools. We'll likely need customer feedback to prioritize.

2. Do we want a single multi-purpose tool, or a larger number of narrower purpose tools?

    Narrow purpose tools.

    Rationale: Originally I was leaning towards multi-purpose but there are various reasons this approach was awkward:
    (a) dotnet's CLI guidance is that the term after dotnet is a 'context', but a multi-purpose tool doesn't make a very useful context. In most cases the verb that would follow needed another sub-context noun to determine the operation. For example 'dotnet diag collect trace' vs. 'dotnet diag collect dump.' Many of the parameters that make sense for a trace don't make sense for a dump and vice-versa so you either end up with workarounds such as a super-set of all possible arguments, arguments that are context sensitive based on earlier arguments, or verbs that are really verb-noun combinations (ie 'collect-dump'). None of these seem to match dotnet conventions.
    (b) Putting the verbs before the sub-context noun makes it hard to determine what each sub-context supports with help commands. For example dotnet analyze dump\_file would be supported but dotnet analyze trace\_file wouldn't (for now at least).
    (c) It encourages higher levels of abstraction and consistency than realistically exist. For example 'dotnet collect' was appealing if you imagined defining a collection plan that could have all sorts of triggers and different collected artifacts that ultimately gets zipped up in a nice package. But building that requires substantial additional work over a basic trace or dump collector.

    Multi-purpose tool did have a few advantages we are giving up:
    (a) Size on disk would have been better. Each tool must include the closure of its non-framework dependencies which means if any tools share a dependency (symbol loading for example) there will be multiple copies of that assembly in the global tools install directory
    (b) A single dotnet install command. We could improve this in dotnet tool install itself, for example by allowing multiple tools to be listed in an install command (similar to many package managers) or supporting a tool meta-package that is just a grouping of related tools. In the meantime we should try to avoid needing too many different tools in the same workflow.

3. Do we have a stripped down stand-alone collector?

    Not at this time, though I still advocate for good layering. There are at least two useful scenarios for this I could see, one is a persistent monitoring agent and the other is on-demand install by support engineers in response to a production problem. In the latter case size on disk is probably only a small concern relative to being able to isolate and customer confidence in the tools. If we could lose the SDK dependency we'd probably be in decent shape there. In the former case size does matter more, but we again be better optimizing for no SDK dependency and FDD deployment before eliminating the analysis components becomes the top size issue. There are also other non-size concerns if we ultimately go this path such as daemonizing the tool, log cycling, auto-triggered collections and remote administration.

4. Do we support command line response files?

    If System.CommandLine supports something default out-of-the-box (and @jonsequitur says it does) then we should use it. We can update the spec once any details are better understood.

5. Do we support '/' style args that are more common on windows or only '--' style args?

    Not at this time (feel like a broken record yet?). We should document and parse all arguments accepting only the single dash or double dash form. For example -h or --help are recognized for help, but /help is not. This keeps us identical to the behavior of other dotnet tools.
    FWIW there might be value in accepting the /arg form of arguments but I'd rather it gets taken up across all dotnet tools or as a generic feature of the command-line parsing library so that we have some degree of standardization. Even if we did start recognizing the /arg form, I still suggest only printing the -/-- forms in the help to prevent clutter.

6. Do we want the tool be 'dotnet' prefixed or use a separate tool name?

    Dotnet prefixed. Our tools have similarities in function and naming to other non-dotnet tools, so including something that evokes 'dotnet' is important to distinguish them. Java established a precedent of putting a 'j' in front of their tool names but if we followed suit ('d' or 'dn' perhaps) I think it would only create confusion with dotnet's convention of using 'dotnet'. We could also try to name the tools something complete different (SOS is one example) and get users to still associate the name with dotnet, but it feels harder and less effective than simply naming 'dotnet' explicitly. Other names appear to be less discoverable, less easily predicted, and if we don't pick catchy names, probably less easily remembered.

## Area specific Questions

### dotnet-counters

- Do we need pre-defined counter sets to make some typical cases easier?

    Not right now, though there is some contention around it. My opinion at the moment is that this feature would probably be useful, but it isn't necessary to create an MVP. If customers try the tool and say it needs profiles I'm happy to add it. @shirhatti and @richlander suggest that we should add it immediately without waiting for that feedback. I'm open to continue the discussion, but I haven't been convinced yet. If nothing changes in the next few weeks on that front then I think we'll wind up shipping a Preview that doesn't have this feature and we'll see what feedback we get.

- Do we want to adjust our provider names?

    SYstem.Runtime is expected to be the final name for the runtime provider. The rest is TBD placeholders.

- What counters exactly are we going to expose and how will we name them?

    TBD. The exact set of counters doesn't matter too much for CLI concerns but a few guidelines that will help.
    1. Providers shouldn't have more than ~40 counters or it will be awkward to display them all in a single console window column
    2. Counter names shouldn't be too long, and they should not use spaces as separators. The '-' character is a better separator
    3. Counters should have separate display names that use spaces and indicate measurement units.

### dotnet-trace

- Add a useful starting point profile for ASP.NET performance investigations

- Does dotnet-trace support other tracing systems or EventPipe only?

    EventPipe only, at least for now. Trying to be a front-end for various other tracing systems (etw, perf, lttng) comes with a substantial increase in complexity and other tools are already doing that such as PerfView and perfcollect. Although creating a unified front-end might have a little value, it is lower priority than doing a good job at our key goal - providing a platform agnostic solution for managed CPU and memory investigations.

- Do we need to support triggers to start/stop collection?

    Not right now. Triggers are a very useful automation primitive and I could imagine wanting to add support in the near future, but many basic scenarios can be resolved without them. They can be added to collect/run commands as options in a fairly non-disruptive way.

- Does dotnet-trace need to support user-specified in-memory buffer sizes?

    We should try to pick some reasonable default for now. If at some point its obvious that isn't working well or it would be easier to make it user-configurable and be done with it we should do so.

- Should we pack traces as zip or diagsession?

    TBD. Diagsession is a Visual Studio container format for profiling data so using that makes engineering a little easier for VS and indicates more clearly that the data is compatible with VS. On the other hand zip is format that nearly every developer should be familiar with, it would serve largely the same role in this case, and it is more easily manipulated by other tools.

- What binaries/symbols do we collect during the pack operation?

    By default we should collect symbols for all non-Microsoft managed binaries refered to in the trace, if they are locatable on the machine. These are symbols that may not be recoverable during trace analysis on another machine because there is no guarantee they are on a symbol server. All Microsoft provided binaries and symbols are available on the symbol server. As of .Net Core 3.0 we will also be fully off of NGEN (assuming our CoreLib plans go forward) and it is much easier to extract symbol<->RVA mapping information for R2R images without requiring a special runtime matched crossgen binary. This should eliminate any need to precompute and package NGEN PDBs.

    We could have non-default options to capture more or fewer symbols based on the users expectations of symbol availability on their analysis machine or their willingness to forego symbolic information to make a trace smaller.

    Todo - design these options.

### dotnet-debug

- Do we need a memory comparison command that is more generic than GC heap? For example VMDiff?

    Not yet, though it really feels pretty useful to me if we expect our customers not to automatically equate memory growth == gc heap growth. I could imagine wanting to add something like this soon.

- Do we have a mechanism to pack dumps similar to our packing of traces?

    Not for now, but probably something we'll want in the near future. Our tools are still picky about needing to analyze dumps on a machine of the same OS so I am assuming most investigation will occur on the same machine.

# Background Info

## Command line tools with similar roles

### perf

Perf is a tool that collects performance traces on Linux in kernel or user-mode. It follows a perf <verb\> <arguments\> convention for its CLI.

     perf

     usage: perf [--version] [--help] COMMAND [ARGS]

     The most commonly used perf commands are:
      annotate        Read perf.data (created by perf record) and display annotated code
      archive         Create archive with object files with build-ids found in perf.data file
      bench           General framework for benchmark suites
      buildid-cache   Manage <tt>build-id</tt> cache.
      buildid-list    List the buildids in a perf.data file
      diff            Read two perf.data files and display the differential profile
      inject          Filter to augment the events stream with additional information
      kmem            Tool to trace/measure kernel memory(slab) properties
      kvm             Tool to trace/measure kvm guest os
      list            List all symbolic event types
      lock            Analyze lock events
      probe           Define new dynamic tracepoints
      record          Run a command and record its profile into perf.data
      report          Read perf.data (created by perf record) and display the profile
      sched           Tool to trace/measure scheduler properties (latencies)
      script          Read perf.data (created by perf record) and display trace output
      stat            Run a command and gather performance counter statistics
      test            Runs sanity tests.
      timechart       Tool to visualize total system behavior during a workload
      top             System profiling tool.

     See 'perf help COMMAND' for more information on a specific command.

Perf stat [options] <command\_line\> [more\_options] runs the command-line, collects performance statistics, and then displays the counters:

    perf stat -B dd if=/dev/zero of=/dev/null count=1000000

    1000000+0 records in
    1000000+0 records out
    512000000 bytes (512 MB) copied, 0.956217 s, 535 MB/s

     Performance counter stats for 'dd if=/dev/zero of=/dev/null count=1000000':

                5,099 cache-misses             #      0.005 M/sec (scaled from 66.58%)
              235,384 cache-references         #      0.246 M/sec (scaled from 66.56%)
            9,281,660 branch-misses            #      3.858 %     (scaled from 33.50%)
          240,609,766 branches                 #    251.559 M/sec (scaled from 33.66%)
        1,403,561,257 instructions             #      0.679 IPC   (scaled from 50.23%)
        2,066,201,729 cycles                   #   2160.227 M/sec (scaled from 66.67%)
                  217 page-faults              #      0.000 M/sec
                    3 CPU-migrations           #      0.000 M/sec
                   83 context-switches         #      0.000 M/sec
           956.474238 task-clock-msecs         #      0.999 CPUs

           0.957617512  seconds time elapsed

Perf record <command\_line\> collects a trace for the given command\_line

    perf record ./noploop 1

    [ perf record: Woken up 1 times to write data ]
    [ perf record: Captured and wrote 0.002 MB perf.data (~89 samples) ]

Perf report [options] reads data from the trace file and renders it to the command-line

    perf report

    # Events: 1K cycles
    #
    # Overhead          Command                   Shared Object  Symbol
    # ........  ...............  ..............................  .....................................
    #
        28.15%      firefox-bin  libxul.so                       [.] 0xd10b45
         4.45%          swapper  [kernel.kallsyms]               [k] mwait_idle_with_hints
         4.26%          swapper  [kernel.kallsyms]               [k] read_hpet
         2.13%      firefox-bin  firefox-bin                     [.] 0x1e3d
         1.40%  unity-panel-ser  libglib-2.0.so.0.2800.6         [.] 0x886f1
         [...]

perf top monitors a machine and shows an updating console UI with the most expensive functions

    perf top
    -------------------------------------------------------------------------------------------------------------------------------------------------------
      PerfTop:     260 irqs/sec  kernel:61.5%  exact:  0.0% [1000Hz
    cycles],  (all, 2 CPUs)
    -------------------------------------------------------------------------------------------------------------------------------------------------------

                samples  pcnt function                       DSO
                _______ _____ ______________________________ ___________________________________________________________

                  80.00 23.7% read_hpet                      [kernel.kallsyms]
                  14.00  4.2% system_call                    [kernel.kallsyms]
                  14.00  4.2% __ticket_spin_lock             [kernel.kallsyms]
                  14.00  4.2% __ticket_spin_unlock           [kernel.kallsyms]
                   8.00  2.4% hpet_legacy_next_event         [kernel.kallsyms]
                   7.00  2.1% i8042_interrupt                [kernel.kallsyms]
                   7.00  2.1% strcmp                         [kernel.kallsyms]
                   6.00  1.8% _raw_spin_unlock_irqrestore    [kernel.kallsyms]
                   6.00  1.8% pthread_mutex_lock             /lib/i386-linux-gnu/libpthread-2.13.so
                   6.00  1.8% fget_light                     [kernel.kallsyms]
                   6.00  1.8% __pthread_mutex_unlock_usercnt /lib/i386-linux-gnu/libpthread-2.13.so
                   5.00  1.5% native_sched_clock             [kernel.kallsyms]
                   5.00  1.5% drm_addbufs_sg                 /lib/modules/2.6.38-8-generic/kernel/drivers/gpu/drm/drm.ko


### Pprof

Pprof is both a runtime library used by golang to collect trace data as well as a CLI tool to visualize that data after it has been collected. The CLI tool is the focus here. Snippets below from https://github.com/google/pprof/blob/main/doc/README.md

Pprof follows the convention Pprof <format\> [options] source.

Unlike many of the other tools there is no need for a verb because it only does one action, reporting on trace data. Source can be an on-disk file or a URL that is streaming the trace data. Format is flexible enough to include text on the console, file based graphics formats, and optionally starting a web browser to visualize content.

Interactive terminal use:

    pprof [options] source

Web Interface

    pprof -http=[host]:[port] [options] source

Common options:

- -flat [default], -cum: Sort entries based on their flat or cumulative weight respectively, on text reports.
- -functions [default], -filefunctions, -files, -lines, -addresses: Generate the report using the specified granularity.
- -noinlines: Attribute inlined functions to their first out-of-line caller. For example, a command like pprof -list foo -noinlines profile.pb.gz can be used to produce the annotated source listing attributing the metrics in the inlined functions to the out-of-line calling line.
- -nodecount= int: Maximum number of entries in the report. pprof will only print this many entries and will use heuristics to select which entries to trim.
- -focus= regex: Only include samples that include a report entry matching regex.
- -ignore= regex: Do not include samples that include a report entry matching regex.
- -show_from= regex: Do not show entries above the first one that matches regex.
- -show= regex: Only show entries that match regex.
- -hide= regex: Do not show entries that match regex.

### Jcmd

Java previously had numerous single-role tools such as jhat, jps, jstack, jinfo, etc that did a variety of diagnostic tasks (respectively they show heap analysis, process status, stacks, and runtime/machine info). Starting in Java8 jcmd, a new multi-role tool, offers a super-set of functionality from all those tools. Snippets below are from https://docs.oracle.com/javase/8/docs/technotes/guides/troubleshoot/tooldescr006.html

Jcmd uses a Jcmd <process\_id/main\_class\> <verb> [options] convention. The set of verbs that are supported varies dynamically depending on the version of the java runtime running in the indicated process. This makes jcmd more of a proxy for a CLI in the runtime than a CLI tool in its own right.

    > jcmd
    5485 sun.tools.jcmd.JCmd
    2125 MyProgram

    > jcmd MyProgram help (or "jcmd 2125 help")
    2125:
    The following commands are available:
    JFR.stop
    JFR.start
    JFR.dump
    JFR.check
    VM.native_memory
    VM.check_commercial_features
    VM.unlock_commercial_features
    ManagementAgent.stop
    ManagementAgent.start_local
    ManagementAgent.start
    Thread.print
    GC.class_stats
    GC.class_histogram
    GC.heap_dump
    GC.run_finalization
    GC.run
    VM.uptime
    VM.flags
    VM.system_properties
    VM.command_line
    VM.version
    help

### WPR

The Windows Performance Recorder is a CLI or GUI tool to capture etw traces on windows. Rather than tracing directly in the WPR process, the trace operates similar to a background service and WPR is a front-end that sends commands to modify its operation. Snippets below from  https://docs.microsoft.com/en-us/previous-versions/windows/it-pro/windows-8.1-and-8/hh448229(v=win.10)

WPR uses the WPR -<verb\> [options] convention.

    wpr {-profiles [<path> [ …]] |
         -start<arguments> |
         -stop<arguments> |
         -cancel |
         -status<arguments> |
         -log<argument> |
         -purgecache |
         -help<arguments> |
         -profiledetails |
         -disablepagingexecutive}

WPR does not support a default file name for saving, the filename must be explicitly provided.

### Perfview

Perfview is CLI or GUI tool that allows collecting, analyzing and viewing ETW traces.

PerfView uses the PerfView <verb/> [options] CLI convention:

    PerfView [DataFile]
        run CommandAndArgs ...
        collect [DataFile]
        start [DataFile]
        stop
        mark [Message]
        abort
        merge [DataFile]
        unzip [DataFile]
        listSessions
        ListCpuCounters
        EnableKernelStacks
        DisableKernelStacks
        HeapSnapshot Process [DataFile]
        ForceGC Process
        HeapSnapshotFromProcessDump ProcessDumpFile [DataFile]
        GuiRun
        GuiCollect
        GuiHeapSnapshot
        UserCommand CommandAndArgs ...
        ...

PerfView has some commands that manipulate an ongoing trace without keeping the PerfView process running (example: start/stop/mark/abort), other commands that capture traces synchronously (example: collect/run), and then further commands that manipulate or view trace data that is already on disk.

When no filename is specified, PerfView saves trace data as PerfViewData.etl[.zip]

### ProcDump

ProcDump is a tool for capturing one or more process dumps (core file on Linux). Historically it was Windows only but it has recently been made cross-platform. Snippets below are from https://docs.microsoft.com/en-us/sysinternals/downloads/procdump

ProcDump uses CLI convention: ProcDump [options]

    usage: procdump [-a] [[-c|-cl CPU usage] [-u] [-s seconds]] [-n exceeds] [-e [1 [-b]] [-f <filter,...>] [-g] [-h]
     [-l] [-m|-ml commit usage] [-ma | -mp] [-o] [-p|-pl counter threshold] [-r] [-t] [-d <callback DLL>] [-64] <[-w]
     <process name or service name or PID> [dump file] | -i <dump file> | -u | -x <dump file> <image file> [arguments]
     >] [-? [ -e]
    Parameter
    Description
    -a
    Avoid outage. Requires -r. If the trigger will cause the target to suspend for a prolonged time due to an exceeded concurrent dump limit, the trigger will be skipped.
    -b
    Treat debug breakpoints as exceptions (otherwise ignore them).
    -c
    CPU threshold at which to create a dump of the process.
    -cl
    CPU threshold below which to create a dump of the process.
    -
d
    Invoke the minidump callback routine named MiniDumpCallbackRoutine of the specified DLL.
    -e
    Write a dump when the process encounters an unhandled exception. Include the 1 to create dump on first chance exceptions.
    -f
    Filter the first chance exceptions. Wildcards (*) are supported. To just display the names without dumping, use a blank ("") filter.
    -g
    Run as a native debugger in a managed process (no interop).
    -h
    Write dump if process has an unresponsive window (does not respond to window messages for at least 5 seconds).
    -i
    Install ProcDump as the AeDebug postmortem debugger. Only -ma, -mp, -d and -r are supported as additional options.
    -l
    Display the debug logging of the process.
    -m
    Memory commit threshold in MB at which to create a dump.
    -ma
    Write a dump file with all process memory. The default dump format only includes thread and handle information.
    -ml
    Trigger when memory commit drops below specified MB value.
    -mp
    Write a dump file with thread and handle information, and all read/write process memory. To minimize dump size, memory areas larger than 512MB are searched for, and if found, the largest area is excluded. A memory area is the collection of same sized memory allocation areas. The removal of this (cache) memory reduces Exchange and SQL Server dumps by over 90%.
    -n
    Number of dumps to write before exiting.
    -o
    Overwrite an existing dump file.
    -p
    Trigger on the specified performance counter when the threshold is exceeded. Note: to specify a process counter when there are multiple instances of the process running, use the process ID with the following syntax: "\Process(<name>_<pid>)\counter"
    -pl
    Trigger when performance counter falls below the specified value.
    -r
    Dump using a clone. Concurrent limit is optional (default 1, max 5).
    CAUTION: a high concurrency value may impact system performance.
    - Windows 7   : Uses Reflection. OS doesn't support -e.
    - Windows 8.0 : Uses Reflection. OS doesn't support -e.
    - Windows 8.1+: Uses PSS. All trigger types are supported.
    -s
    Consecutive seconds before dump is written (default is 10).
    -t
    Write a dump when the process terminates.
    -u
    Treat CPU usage relative to a single core (used with -c).
    As the only option, Uninstalls ProcDump as the postmortem debugger.
    -w
    Wait for the specified process to launch if it's not running.
    -x
    Launch the specified image with optional arguments. If it is a Store Application or Package, ProcDump will start on the next activation (only).
    -64
    By default ProcDump will capture a 32-bit dump of a 32-bit process when running on 64-bit Windows. This option overrides to create a 64-bit dump. Only use for WOW64 subsystem debugging.
    -?
    Use -? -e to see example command lines.

When creating dumps, procdump uses a default output format of PROCESSNAME\_YYMMDD\_HHMMSS.dmp
where:

    PROCESSNAME = Process Name
    YYMMDD = Year/Month/Day
    HHMMSS = Hour/Minute/Second

### Perfmon

Perfmon is a Windows GUI tool that shows interactive performance counters and some reports of system performance. It has a minimal CLI that simply launches different GUI views.

perfmon </res|report|rel|sys>

### LTTNG

TODO
