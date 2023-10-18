This documentation is now being maintained at [dotnet-gcdump](https://github.com/dotnet/docs/blob/a201d22d8c33fcb77af093edb96d0fe669e0e491/docs/core/diagnostics/dotnet-gcdump.md). This doc is no longer being updated.

# Heap Analysis Tool (dotnet-gcdump)

NOTE: This documentation page may contain information on some features that are still work-in-progress. For most up-to-date documentation on released version of `dotnet-gcdump`, please refer to [its official documentation](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump) page.

## Intro

The dotnet-gcdump tool is a cross-platform CLI tool that collects gcdumps of live .NET processes. It is built using the EventPipe technology which is a cross-platform alternative to ETW on Windows. Gcdumps are created by triggering a GC
in the target process, turning on special events, and regenerating the graph of object roots from the event stream. This allows for gcdumps to be collected while the process is running with minimal overhead. These dumps are useful for
several scenarios:

* comparing the number of objects on the heap at several points in time
* analyzing roots of objects (answering questions like, "what still has a reference to this type?")
* collecting general statistics about the counts of objects on the heap.

dotnet-gcdump can be used on Linux, Mac, and Windows with runtime versions 3.1 or newer.

## Installing dotnet-gcdump

The first step is to install the dotnet-gcdump CLI global tool.

```cmd
$ dotnet tool install --global dotnet-gcdump
You can invoke the tool using the following command: dotnet-gcdump
Tool 'dotnet-gcdump' (version '3.0.47001') was successfully installed.
```

## Using dotnet-gcdump

In order to collect gcdumps using dotnet-gcdump, you will need to:

- First, find out the process identifier (pid) of the target .NET application.

  - On Windows, there are options such as using the task manager or the `tasklist` command in the cmd prompt.
  - On Linux, the trivial option could be using `pidof` in the terminal window.

You may also use the command `dotnet-gcdump ps` command to find out what .NET processes are running, along with their process IDs.

- Then, run the following command:

```cmd
dotnet-gcdump collect --process-id <PID>

Writing gcdump to 'C:\git\diagnostics\src\Tools\dotnet-gcdump\20191023_042913_24060.gcdump'...
    Finished writing 486435 bytes.
```

- Note that gcdumps can take several seconds depending on the size of the application

## Viewing the gcdump captured from dotnet-gcdump

On Windows, `.gcdump` files can be viewed in [PerfView](https://github.com/microsoft/perfview) for analysis or in Visual Studio. On non-Windows platforms, [dotnet-heapview](https://github.com/1hub/dotnet-heapview) can be used as a simple viewer.

You can collect multiple `.gcdump`s and open them simultaneously in Visual Studio to get a comparison experience.

## Known Caveats

- There is no type information in the gcdump

Prior to .NET Core 3.1, there was an issue where a type cache was not cleared between gcdumps when they were invoked with EventPipe. This resulted in the events needed for determining type information not being sent for the second and subsequent gcdumps. This was fixed in .NET Core 3.1-preview2.


- COM and static types aren't in the gcdump

Prior to .NET Core 3.1-preview2, there was an issue where static and COM types weren't sent when the gcdump was invoked via EventPipe. This has been fixed in .NET Core 3.1-preview2.

## *dotnet-gcdump* help

```cmd
collect:
  Collects a gcdump from a currently running process

Usage:
  dotnet-gcdump collect [options]

Options:
  -p, --process-id <pid>             The process to collect the gcdump from
  -n, --name <name>                  The name of the process to collect the gcdump from.
  -o, --output <gcdump-file-path>    The path where collected gcdumps should be written. Defaults to '.\YYYYMMDD_HHMMSS_<pid>.gcdump'
                                     where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full path
                                     and file name of the dump.
  -v, --verbose                      Output the log while collecting the gcdump
  -t, --timeout <timeout>            Give up on collecting the gcdump if it takes longer the this many seconds. The default value is 30s
```
