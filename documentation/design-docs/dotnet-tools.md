Capturing some comments from https://github.com/dotnet/diagnostics/issues/85 as a starting point
we can edit from.

@davidfowl wrote:

@shirhatti We're thinking it should be part of dotnet-collect since all of the flags and infrastructure would likely be the same and it should have a "top" like interface. 

![image](https://user-images.githubusercontent.com/95136/49297973-09575400-f471-11e8-99ec-823e616eafa2.png)

We'll need to decide what things we show (aggregations and counters)




@shirhatti wrote:

```
NAME

    dotnet-collect - Collect diagnostic information from a .NET process

SYNOPSIS

    dotnet collect [-v, --version] [-h, --help]
                   [-p, --process-id]
                   [-o, --output]
                   <command> [<args>]

OPTIONS

    -v, --version
        Prints the version of the dotnet collect utility.

    -h, --help
        Prints the synopsis and a list of the most commonly used commands. 

    -p, --process-id <PROCESS_ID>
        The process id of the process you want to collect diagnostic information
        from.
    
    -o, --output <OUTPUT_DIRECTORY>
        The output directory where this diagnostic data should be written to.


================================================================================

NAME

    dotnet-collect-dump - Collect a process dump 

SYNOPSIS

    dotnet collect dump [-h, --help]
                        
DESCRIPTION

    On Windows, dotnet-collect-dump collects a Windows minidump.
    On Linux, dotnet-collect-dump collects a core dump using createdump.

OPTIONS

    -h, --help
        Prints the synopsis and a list of the most commonly used commands. 

================================================================================

NAME

    dotnet-collect-trace - Collect a trace

SYNOPSIS

    dotnet collect dump [-h, --help]
                        [--provider]
                        [--buffer]
                        

OPTIONS

    -h, --help
        Prints the synopsis and a list of the most commonly used commands.

    --provider <PROVIDER_SPEC>
        An EventPipe provider to enable.
        A string in the form '<provider name>:<keywords>:<level>'. 
    
    --buffer <BUFFER_SIZE_IN_MB>
        The size of the in-memory circular buffer in megabytes.

================================================================================

NAME

    dotnet-monitor

SYNOPSIS

    dotnet monitor [-v, --version] [-h, --help]
                   [--provider]
                   [--buffer]
                        

OPTIONS

    -v, --version
        Prints the version of the dotnet collect utility.

    -h, --help
        Prints the synopsis and a list of the most commonly used commands.

    --provider <PROVIDER_SPEC>
        An EventPipe provider to enable.
        A string in the form '<provider name>:<keywords>:<level>'. 
    
    --buffer <BUFFER_SIZE_IN_MB>
        The size of the in-memory circular buffer in megabytes.

================================================================================
```