# Diagnostic Tools Extensibility

This document describes a mechanism to allow first and third party users to add custom commands and services to `dotnet-dump` and `SOS` on the supported debuggers. Such extensibility has been a frequent ask from companies like Criteo and some teams at Microsoft. The goal is to write the code for a command once and have it run under all the supported debuggers, including dotnet-dump.

Internally, the ability to host commands like the future `gcheapdiff` under dotnet-dump, lldb and cdb/windbg will be invaluable for the productivity of developers in the ecosystem. Implementing new commands and features in C# is far easier and more productive for the interested parties. Other people on .NET team and in the community are more likely to contribute improvements to our tools, similar to what Stephen did with `dumpasync`. Unlike the plugin situation, if they contribute directly to our repo then the improvements will automatically flow to all customers and provide broader value.

This effort is part of the "unified extensiblity" models - where various teams are coming together to define a common abstraction across all debuggers and debugger like hosts (dotnet-dump). Services such as Azure Watson could use this infrastructure to write a commands akin to `!analyze` and other analysis tools using a subset of the DAC - provided as a service - to do some unhandled exception and stack trace triage.

## Goals

- Provide a simple set of services that hosts can implement and commands/services use.
- Easy use of the ClrMD API in commands and services.
- Host the same commands/command assemblies under various "hosts" like:
   - The dotnet-dump REPL
   - The lldb debugger
   - The Windows debuggers (windbg/cdb)
   - Visual Studio
 - Create various "target" types from the command line or a command from:
   - Windows minidumps
   - Linux coredumps
   - Live process snapshots 
 
## Customer Value

- Improve our CSS engineer experience by providing commands in Visual Studio that keep them from needing to switch to windbg. 
    - Commands that CSS devs would find useful in Visual Studio that can't be done in VS any other way:
        - !GCHandles - Provides statistics about GCHandles in the process.
        - !ThreadPool - This command lists basic information about the ThreadPool, including the number of work requests in the queue, number of completion port threads, and number of timers.
        - !SaveModule - This command allows you to take a image loaded in memory and write it to a file. This is especially useful if you are debugging a full memory dump, and saves a PE module to a file from a dump, and don't have the original DLLs or EXEs.
        - rest of list TBD.
        
- Enables support for Azure Geneva diagnostics which is using the Native AOT corert based runtime. This infrastructure will allow the necessary set of SOS commands to be written and executed across the support platforms (Windows windbg and Linux lldb).

- Improve our internal .NET team productivity and inner loop development by providing these managed commands and the classical native SOS commands under debuggers like Visual Studio. See issue [#1397](https://github.com/dotnet/diagnostics/issues/1397).  
      
- This plan would allow these ClrMD based commands to run across all our debuggers (dotnet-dump, windbg, lldb and Visual Studio):
    - Criteo's 5 or so extension commands:
        - timerinfo - display running timers details.
        - pstack - display the parallel stacks
        - rest of list TBD
 
- Existing ClrMD commands like "clrmodules" which displays the version info of the managed assemblies in the dump or process.

- List of issues that will be addressed by this work or has inspired it:
    - [#1397](https://github.com/dotnet/diagnostics/issues/1397) and [#40182](https://github.com/dotnet/runtime/issues/40182) "SOS Plugin for Visual Studio". Create a VS package that allows the above extension commands to be run and various native SOS commands.
    - [#565](https://github.com/dotnet/diagnostics/issues/565) "Using SOS programmatically". This plan will help enable the work described in this issue.
    - [#1031](https://github.com/dotnet/diagnostics/issues/1031) "Ability to load extensions in dotnet-dump analyze". This refers to loading "sosex" and "mex" in dotnet-dump. This plan would make it easier to do this but does not actually include it.
    - [#194](https://github.com/dotnet/diagnostics/issues/194) "Implement `gcheapdiff` dotnet-dump analyze command". We haven't had a lot of feedback on whether this purposed command is useful. This issue did inspired the "multi-target" part of this plan i.e. the ability to load/analyze two dumps in dotnet-dump at the same time.

## Road Map

1. Create a VS host/target package allowing SOS and the extension commands to be run from VS using the Concord API.
2. Add Linux, MacOS and Windows live snapshot targets. ClrMD already has support for them; just need to implement the target factory.

## Design

The design consists of abstractions for the debugger or code hosting this infrastructure, one or more targets that represent the dump or process being targeted, one or more .NET runtimes in the target process (both desktop framework and .NET Core runtimes are supported) and various services that are avaiiable for commands, other services and the infrastructure itself. 

Each hosting environment will have varing set requirements for the services needed. Other than the basic set of target, memory, and module (TBD), services can be optional. For example, hosting the native SOS code requires a richer set of interfaces than ClrMD based commands. 

- ClrMD commands and possible analysis engine
  - Basic target info like architecture, etc.
  - Console service
  - Memory services
  - Simple set of module services
  - Simple set of thread services
  - Mechanism to expose ClrInfo/ClrRuntime instances
- Native AOT commands
  - Memory services
  - Console service
  - Command service
  - Simple module services
  - Simple set of symbol services
  - Mechanism to expose Runtime Snapshot Parse API.
- dotnet-dump and VS that host the native SOS code
  - Target and runtime services
  - Console service
  - Command service
  - Memory services
  - Richer module services
  - Thread services
  - Symbol and download services
 
The threading model is single-threaded mainly because native debuggers like dbgeng are basically single-threaded and using async makes the implementation and the over all infrastructre way more complex. 

#### Interface Hierarchy

- IHost
  - Global services 
    - IConsoleService
    - ICommandService
    - ISymbolService
    - IDumpTargetFactory
    - IProcessSnapshotTargetFactory
  - ITarget
    - Per-target services
        - IRuntimeService
            - IRuntime
              - ClrInfo
              - ClrRuntime
              - Runtime Snapshot Parse instance
      - IMemoryService
      - IModuleService
        - IModule 
      - IThreadService
        - IThread
      - SOSHost
      - ClrMDHelper

## Hosts

The host is the debugger or program the command and the infrastructure runs on. The goal is to allow the same code for a command to run under different programs like the dotnet-dump REPL, lldb and Windows debuggers. Under Visual Studio the host will be a VS extension package. 

#### IHost

The host implements this interface to provide to the rest the global services, current target and the type of host. 

See [IHost.cs](../../src/Microsoft.Diagnostics.DebugServices/IHost.cs) and [host.h](../../src/SOS/inc/host.h) for details.

## Targets

A data target represents the dump, process snapshot or live target session. It provides information and services for the target like architecture, native process, thread, and module services. For commands like gcheapdiff, the ability to open and access a second dump or process snapshot is necessary.

Because of the various hosting requirements, ClrMD's IDataReader and DataTarget should not be exposed directly and instead use one of the following interfaces and services. This allows this infrastructure provide extra functionality to ClrMD commands like the address space module mapping. 

Targets are only created when there is a valid process. This means the ITarget instance in the managed infrastructure and in the native SOS code can be null. On lldb, the current target state is checked and updated if needed before each command executed or callback to the native SOS or the managed infrastructure is made.

The target interface provides a "Flush" callback used to clear any cached state in the per-target services when the native debuggers continue/stop. It is up to each service to register for the OnFlush target event and clear any cached state. On dbgeng an event callback (ChangeEngineState) is used that fires when the debugger stops to invoke the OnFlush event.  On lldb, the stop id provided by the lldb API is used. It is checked each time a command is executed or a callback invoked. Any time it changes, the target's OnFlush event is invoked.

The "gcdump" target is a possible target that allows gcdump specific services and commands to be executed in a REPL. None of the SOS commands or ClrMD services will work but it will be easy to provide services and commands specific to the gcdump format. This may require some kind of command filtering by the target or the target provides the command service.

#### ITarget

This interface abstracts the data target and adds value with things like per-target services and the context related state like the current thread. 

See [ITarget.cs](../../src/Microsoft.Diagnostics.DebugServices/ITarget.cs) and [target.h](../../src/SOS/inc/target.h) for details.

## Services

Everything a command or another service needs are provided via a service. There are global, per-target and per-command invocation services. Services like the command, console and target service and target factories are global. Services like the thread, memory and module services are per-target. Services like the current ClrRuntime instance are per-command invocation because it could change when there are multiple runtimes in a process.

For Windbg/cdb, these services will be implemented on the dbgeng API.

For lldb, these services will be implemented on the lldb extension API via some new pinvoke wrappers.

For Visual Studio, these services will be implemented on the Concord API in VS package. The hardest part of this work is loading/running the native SOS and DAC modules in a 64bit environment. 

### IDumpTargetFactory

This global service allows dump ITargets to be created.

See [IDumpTargetFactory.cs](../../src/Microsoft.Diagnostics.DebugServices/IDumpTargetFactory.cs) for more details.

### ICommandService

This service provides the parsing, dispatching and executing of standardized commands. It is implemented using System.Commandline but there should be no dependencies on System.CommandLine exposed to the commands or other services. It is an implementation detail.

See [ICommandService.cs](../../src/Microsoft.Diagnostics.DebugServices/ICommandService.cs)

### IConsoleService

Abstracts the console output across all the platforms.

See [IConsoleService.cs](../../src/Microsoft.Diagnostics.DebugServices/IConsoleService.cs).

### IMemoryService

Abstracts the memory related functions.

There are two helper IMemoryService implementations PEImageMappingMemoryService and MetadataMappingMemoryService. They are used to wrap the base native debugger memory service implementation. 

PEImageMappingMemoryService is used in dotnet-dump for Windows targets to mapping PE images like coreclr.dll into the memory address space the module's memory isn't present. It downloads and loads the actual module and performs the necessary fix ups.

MetadataMappingMemoryService is only used for core dumps when running under lldb to map the managed assemblies metadata into address space. This is needed because the way lldb returns zero's for invalid memory for dumps generated with createdump on older runtimes (< 5.0). 

The address sign extension plan for 32 bit processors (arm32/x86) is that address are masked on entry to the managed infrastructure from DAC or DBI callbacks or from the native SOS code in SOS.Hosting. If the native debugger that the infrastructure is hosted needs addresses to be signed extended like dbgeng, it will happen in the debugger services layer (IDebuggerService).

See [IMemoryService.cs](../../src/Microsoft.Diagnostics.DebugServices/IMemoryService.cs).

### IThreadService and IThread

Abstracts the hosting debuggers native threads. There are functions to enumerate, get details and context about native threads.

See [IThreadService.cs](../../src/Microsoft.Diagnostics.DebugServices/IThreadService.cs) and [IThread.cs](../../src/Microsoft.Diagnostics.DebugServices/IThread.cs)

### IModuleService and IModule

Abstracts the modules in the target. Provides the details the name, base address, build id, version, etc. Some targets this includes both native and managed modules (Windows dbgeng, Linux dotnet-dump ELF dumps) but there are hosts/targets (Linux/MacOS lldb) that only provide the native modules. 

One issues that may need to be addressed is that some platforms (MacOS) have non-contiguous memory sections in the module so the basic ImageSize isn't enough. May need (maybe only internally) need some concept of "sections" (address, size) and/or "header size".  One of the main uses of the ImageBase/ImageSize is to create a memory stream of the PE or module header to extract module details like version, build id, or search for embedded version string.

See [IModuleService.cs](../../src/Microsoft.Diagnostics.DebugServices/IModuleService.cs) and [IModule.cs](../../src/Microsoft.Diagnostics.DebugServices/IModule.cs).

### ISymbolService

This service provides the symbol store services like the functionality that the static APIs in SOS.NETCore's SymbolReader.cs does now. The SOS.NETCore assembly will be removed and replaced with this symbol service implementation. Instead of directly creating delegates to static functions in this assembly, there will be a symbol service wrapper that provides these functions to the native SOS.

The current implementation of the symbol downloading support in SOS.NETCore uses sync over async calls which could cause problems in more async hosts (like VS) but it hasn't caused problems in dotnet-dump so far. To fix this there may be work in the Microsoft.SymbolStore (in the symstore repo) component to expose synchronous APIs.

See [ISymbolService.cs](../../src/Microsoft.Diagnostics.DebugServices/ISymbolService.cs) for more details.

### IRuntimeService/IRuntime

This service provides the runtime instances in the target process. The IRuntime abstracts the runtime providing the ClrInfo and ClrRuntime instances from ClrMD and the Native AOT runtime snapshot parser instance in the future.

See [IRuntimeService.cs](../../src/Microsoft.Diagnostics.DebugServices/IRuntimeService.cs), [IRuntime.cs](../../src/Microsoft.Diagnostics.DebugServices/IRuntime.cs) and [runtime.h](../../src/SOS/inc/runtime.h) for details.

### SOSHost

This service allows native SOS commands to be executed under hosts like dotnet-dump and VS. This should probably have an interface to abstract it (TBD).

Some of the native SOS's "per-target" globals will need to be queried from this infrastructure instead being set once on initialization. This includes things like the DAC module path, DBI module path, the temp directory path (since it contains the process id), etc. It will provide native versions of the IHost, ITarget and IRuntime interfaces to the native SOS to do this.

### IHostServices

This interface provides services to the native SOS/plugins code. It is a private interface between the native SOS code and the SOS.Extensions host. There are services to register the IDebuggerService instance, dispatch commands and create/detroy target instance.

[hostservices.h](../../src/SOS/inc/hostservices.h) for details.

### IDebuggerService

This native interface is what the SOS.Extensions host uses to implement the above services. This is another private interface between SOS.Extensions and the native lldb plugin or Windows SOS native code.

[debuggerservice.h](../../src/SOS/inc/debuggerservice.h) for details.

## Projects and Assemblies

### SOS.Extensions (new)

This assembly implements the host, target and services for the native debuggers (dbgeng, lldb). It provides the IHostServices to the native "extensions" library which registers the IDebuggerService used by the service implementations.

### The "extensions" native library (new)

This is the native code that interops with the managed SOS.Extensions to host the native debuggers. It sets up the managed runtime (.NET Core on Linux/MacOS or desktop on Windows) and calls the SOS.Extensions initialization entry point. It is linked into the lldbplugin on Linux/MacOS and into SOS.dll on Windows. 

### SOS.Hosting

This contains the hosting support used by the dotnet-dump REPL and an eventual Visual Studio package to run native SOS commands without a native debugger like dbgeng or lldb.

### SOS.NETCore (going away)

This currently contains the symbol download and portable PDB source/line number support for the native SOS code. It will be replaced by the symbol service and wrappers (see ISymbolService).

### Microsoft.Diagnostics.DebugServices

Contains definations and abstractions for the various services interfaces. 

### Microsoft.Diagnostics.DebugServices.Implementation (new)

Contains the common debug services implementations used by dotnet-dump and SOS.Extensions (dbgeng/lldb) hosts.

### Microsoft.Diagnostics.ExtensionCommands (new)

Contains the common commands shared with dotnet-dump and the SOS.Extensions hosts.

### Microsoft.Diagnostics.Repl

The command and console service implemenations. 

### dotnet-dump

The dump collection and analysis REPL global tools. It hosts the extensions layer and debug services using ClrMD's Linux and Windows minidump data readers.

### lldbplugin

The lldb plugin that provides debugger services (IDebuggerServices) to SOS.Extensions and LLDBServices to native SOS. It displays both the native SOS and new managed extension commands. It initializes the managed hosting layer via the "extensions" native library.

### Strike (SOS)

Native SOS commands and code.

On Windows, it provides the debugger services (IDebuggerServices) to SOS.Extensions and initializes the managed hosting layer via the "extensions" native library.
 
