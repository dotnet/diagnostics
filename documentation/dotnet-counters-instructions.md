This documentation is now being maintained here: [dotnet-counters](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters). This doc is no longer being updated.

# dotnet-counters

NOTE: This documentation page may contain information on some features that are still work-in-progress. For most up-to-date documentation on released version of `dotnet-counters`, please refer to [its official documentation](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) page.

## Intro

dotnet-counters is a performance monitoring tool for ad-hoc health monitoring or 1st level performance investigation. It can observe performance counter values that are published via `EventCounter` API (https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventcounter). For example, you can quickly monitor things like the CPU usage or the rate of exceptions being thrown in your .NET Core application to see if there is anything suspiscious before diving into more serious performance investigation using PerfView or dotnet-trace.


## Install dotnet-counters

```
dotnet tool install --global dotnet-counters
```


## Using dotnet-counters

*SYNOPSIS*

    dotnet-counters [--version]
                    [-h, --help]
                    <command> [<args>]

*OPTIONS*

    --version
        Display the version of the dotnet-counters utility.

    -h, --help
        Show command line help

*COMMANDS*

    list      Display a list of counter names and descriptions
    ps        Display a list of dotnet processes that can be monitored
    monitor   Display periodically refreshing values of selected counters
    collect   Periodically collect selected counter values and export them into a specified file format for post-processing.

*PS*

    dotnet-counters ps

    Display a list of dotnet processes that can be monitored.

    Examples:
      > dotnet-counters ps

     15683 WebApi     /home/suwhang/repos/WebApi/WebApi
     16324 dotnet     /usr/local/share/dotnet/dotnet

*LIST*

    dotnet-counters list [-h|--help]

    Display a list of counter names and descriptions, grouped by provider.

    -h, --help
        Show command line help

    Examples:
      > dotnet-counters list

    Showing well-known counters only. Specific processes may support additional counters.
    System.Runtime
        cpu-usage                    Amount of time the process has utilized the CPU (ms)
        working-set                  Amount of working set used by the process (MB)
        gc-heap-size                 Total heap size reported by the GC (MB)
        gen-0-gc-count               Number of Gen 0 GCs / sec
        gen-1-gc-count               Number of Gen 1 GCs / sec
        gen-2-gc-count               Number of Gen 2 GCs / sec
        exception-count              Number of Exceptions / sec

*MONITOR*

    ### Examples:

    1. Monitoring all counters from `System.Runtime` at a refresh interval of 3 seconds:

      > dotnet-counters monitor --process-id 1902 System.Runtime

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

      > dotnet-counters monitor --process-id 1902 --refresh-interval 5 System.Runtime[cpu-usage,gc-heap-size,exception-count]

    Press p to pause, r to resume, q to quit.
      System.Runtime:
        CPU Usage (%)                                 24
        GC Heap Size (MB)                            811
        Number of Exceptions / sec                     4

    3. Monitoring EventCounter values from user-defined EventSource: (see https://github.com/dotnet/corefx/blob/main/src/System.Diagnostics.Tracing/documentation/EventCounterTutorial.md on how to do this.0)

      > dotnet-counters monitor --process-id 1902 Samples-EventCounterDemos-Minimal

    Press p to pause, r to resume, q to quit.
        request                                      100

    4. Launch `my-aspnet-server.exe` with `arg1` and `arg2` as command-line arguments and monitor its GC heap size and working set from startup.

    NOTE: This works for apps running .NET 5.0 or later only.

     ```console
    > dotnet-counters monitor --counters System.Runtime[assembly-count] -- my-aspnet-server.exe arg1 arg2

    Press p to pause, r to resume, q to quit.
        Status: Running

    [System.Runtime]
        GC Heap Size (MB)                                 39
        Working Set (MB)                                  59
    ```

    ### Syntax:

    dotnet-counters monitor [-h||--help]
                            [-p|--process-id <pid>]
                            [--refresh-interval <sec>]
                            [--counters <counters>]
                            [-- <command>]
    
    Display periodically refreshing values of selected counters
    
    -h, --help
        Show command line help
    
    -p,--process-id
        The ID of the process that will be monitored

    --refresh-interval
        The number of seconds to delay between updating the displayed counters
    
    --counters
        A comma separated list of counters. Counters can be specified provider_name[:counter_name]. If the
        provider_name is used without a qualifying counter_name then all counters will be shown. To discover
        provider and counter names, use the list command.

    -- <command> (for target applications running .NET 5.0 or later only)
        After the collection configuration parameters, the user can append `--` followed by a command to start a .NET application with at least a 5.0 runtime. `dotnet-counters` will launch a process with the provided command and collect the requested metrics.

*COLLECT*

### Examples: 

1. Collect the runtime performance counters at a refresh interval of 10 seconds and export it as a JSON file named "test.json".

```
    dotnet-counters collect --process-id 863148 --refresh-interval 10 --output test --format json
```

2. Collect the runtime performance counters as well as the ASP.NET hosting performance counters at the default refresh interval (1 second) and export it as a CSV file named "mycounter.csv". 

```
    dotnet-counters collect --process-id 863148 --output mycounter --format csv System.Runtime Microsoft.AspNetCore.Hosting
```

3. Launch `my-aspnet-server` and collect the assembly-count counter from its startup.

  NOTE: This works for apps running .NET 5.0 or later only.

```bash
$ dotnet-counters monitor --counters System.Runtime[assembly-count] -- my-aspnet-server.exe
```

    ### Syntax:

    dotnet-counters collect [-h||--help]
                            [-p|--process-id <pid>]
                            [-n|--name <name>]
                            [-o|--output <name>]
                            [--format <csv|json>]
                            [--refresh-interval <sec>]
                            [--counters <counters>]
                            [-- <command>]
    
    Periodically collect selected counter values and export them into a specified file format for post-processing.
    
    -h, --help
        Show command line help
    
    -p,--process-id
        The ID of the process that will be monitored

    -n,--name
        The name of the process that will be monitored. This can be specified in place of process-id.

    -o, --output
        The name of the output file

    --format
        The format to be exported. Currently available: csv, json

    --refresh-interval
        The number of seconds to delay between updating the displayed counters
    
    --counters
        A comma separated list of counters. Counters can be specified provider_name[:counter_name]. If the
        provider_name is used without a qualifying counter_name then all counters will be shown. To discover
        provider and counter names, use the list command.

    -- <command> (for target applications running .NET 5.0 or later only)
        After the collection configuration parameters, the user can append `--` followed by a command to start a .NET application with at least a 5.0 runtime. `dotnet-counters` will launch a process with the provided command and collect the requested metrics.
