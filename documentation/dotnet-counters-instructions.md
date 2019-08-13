# dotnet-counters


## Intro

dotnet-counters is a performance monitoring tool for ad-hoc health monitoring or 1st level performance investigation. It can observe performance counter values that are published via `EventCounter` API (https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventcounter). For example, you can quickly monitor things like the CPU usage or the rate of exceptions being thrown in your .NET Core application to see if there is anything suspiscious before diving into more serious performance investigation using PerfView or dotnet-trace.


## Install dotnet-counters

```
dotnet tool install --global dotnet-counters --version 3.0.0-preview8.19412.1
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
    monitor   Display periodically refreshing values of selected counters

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

      > dotnet-counters monitor --process-id 1902 System.Runtime[cpu-usage,gc-heap-size,exception-count]

    Press p to pause, r to resume, q to quit.
      System.Runtime:
        CPU Usage (%)                                 24
        GC Heap Size (MB)                            811
        Number of Exceptions / sec                     4

    3. Monitoring EventCounter values from user-defined EventSource: (see https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.Tracing/documentation/EventCounterTutorial.md on how to do this.0)

      > dotnet-counters monitor --process-id 1902 Samples-EventCounterDemos-Minimal

    Press p to pause, r to resume, q to quit.
        request                                      100


    ### Syntax:

    dotnet-counters monitor [-h||--help]
                            [-p|--process-id <pid>]
                            [--refreshInterval <sec>]
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

