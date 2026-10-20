# Diagnostic Tools Extensibility

This document describes a mechanism to allow first and third party users to add custom commands and services to `dotnet-dump` and `SOS` on the supported debuggers. Such extensibility has been a frequent ask from companies like Criteo and some teams at Microsoft. The goal is to write the code for a command once and have it run under all the supported debuggers, including dotnet-dump.

The ability to host commands like the future `gcheapdiff` under dotnet-dump, lldb, cdb/windbg and Visual Studio will be invaluable for the productivity of developers in the ecosystem. Implementing new commands and features in C# is far easier and more productive for the interested parties. Other people on .NET team and in the community are more likely to contribute improvements to our tools, similar to what Stephen did with `dumpasync`. Unlike the plugin situation, if they contribute directly to our repo then the improvements will automatically flow to all customers and provide broader value.

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
   - Linux and MacOS coredumps
   - Live process snapshots 
 
## Customer Value

- Improve our CSS engineer experience by providing commands in Visual Studio that keep them from needing to switch to windbg. 
    - Commands that CSS devs would find useful in Visual Studio that can't be done in VS any other way:
        - !GCHandles - Provides statistics about GCHandles in the process.
        - !ThreadPool - This command lists basic information about the ThreadPool, including the number of work requests in the queue, number of completion port threads, and number of timers.
        - !SaveModule or !SaveAllModules ([#3138](https://github.com/dotnet/diagnostics/issues/3138)) - This command allows you to take a image loaded in memory and write it to a file. This is especially useful if you are debugging a full memory dump, and saves a PE module to a file from a dump, and don't have the original DLLs or EXEs.
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

## Design

The design consists of abstractions for the debugger or code hosting this infrastructure, one or more targets that represent the dump or process being targeted, one or more .NET runtimes in the target process (both desktop framework and .NET Core runtimes are supported) and various services that are avaiiable for commands, other services and the infrastructure itself. 

Each hosting environment will have varing set requirements for the services needed. Other than the basic set of target, memory, thread and module, most services can be optional. For example, hosting the native SOS code requires a richer set of interfaces than ClrMD based commands. 

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
    - IConsoleFileLoggingService
    - ICommandService
    - ISymbolService
    - IContextService
    - IDumpTargetFactory
    - IProcessAttachTargetFactory
    - IDiagnositcLoggingService
    - IServiceManager/IServiceContainer
  - ITarget
    - Per-target services
        - IRuntimeService
            - IRuntimeProvider
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

Each contributing assembly has one registration file containing an `IDiagnosticsModule` implementation. `CoreModule`, `SOSHostingModule`, and `AnalysisModule` contain the built-in service and command registrations and do not use module initializers. `Host` explicitly registers the core module; dotnet-dump and SOS.Extensions each directly register native SOS hosting before managed analysis, preserving command fallback order. Dotnet-dump registers its host-specific SOS dispatch and exit commands together after module commands so its host commands take precedence over contributed commands with the same names or aliases.

Additional assemblies advertise module factories to `DiagnosticsModuleCatalog` from `[ModuleInitializer]` methods before the host creates its global service container. Initializers advertise factories without constructing modules or services. Each service manager creates its own additional module instances and registers their services after the built-ins, before finalization; the host then calls `ServiceManager.RegisterCommands` to register commands from those same instances. Both built-in instances and additional factories use `ServiceManager.RegisterModule` to register services and retain the module for command registration. Registration does not scan assemblies, attributes, or types. Runtime extension DLL discovery and loading have been removed: `DOTNET_DIAGNOSTIC_EXTENSIONS` and the `extensions` subdirectory are no longer used.

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

This interface abstracts the data target, contains process architecture and platform info and adds value with things like per-target services like IMemoryService, IModuleService, IThreadService, IRuntimeService, etc.

See [ITarget.cs](../../src/Microsoft.Diagnostics.DebugServices/ITarget.cs) and [target.h](../../src/SOS/inc/target.h) for details.

## Services

Everything a command or another service needs are provided via a service. There are global, target, module, thread, runtime and context services. Services like the command, console and target service and target factories are global. Services like the thread, memory and module services are per-target. Services like the current ClrRuntime instance are per-runtime because it could change when there are multiple runtimes in a process.

For Windbg/cdb and lldb, these services are implemented on the dbgeng API via the DebuggerServices pinvoke wrapper and interfaces.

For Visual Studio, the base memory, module, thread services will be implemented on the Concord API in VS package. The rest of the services are implemented by the common code in Microsoft.Diagnostics.DebugServices.Implementation. The hardest part of this work is loading/running the native SOS and DAC modules in a 64bit environment. 

Services can be registered to contain common code for commands like [ClrMDHelper](../../src/Microsoft.Diagnostics.ExtensionCommands/ClrMDHelper.cs) or host or target functionality like ISymbolService or IMemoryService.

The [IServiceRegistry](../../src/Microsoft.Diagnostics.DebugServices/IServiceRegistry.cs) contract registers lazy service factories and provider factories through generic `AddServiceFactory<TService>` and `AddProviderFactory<TProvider>` methods. Their `Func<IServiceProvider, T>` delegates enforce the registered result type at compile time and may return `null` for unavailable services. `ServiceScope` defines where in the service hierarchy (global, context, target, runtime, thread, or module) a service instance is available. Ordinary services use typed registrations, with constructor dependencies supplied by `ActivatorUtilities` from `Microsoft.Extensions.DependencyInjection.Abstractions`:

Typed registration creates and caches an activation delegate, not a service instance: each container lazily creates and caches its instances. Provider factories remain an ordered collection, allowing `IRuntimeService` to enumerate multiple `IRuntimeProvider` implementations.

An assembly registers its services and commands together in one module file. The base host does not need to know the contributed types:

```csharp
using Microsoft.Diagnostics.DebugServices;
using System.Runtime.CompilerServices;

public sealed class MyModule : IDiagnosticsModule
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        DiagnosticsModuleCatalog.Register(() => new MyModule());
    }

    public void RegisterServices(IServiceRegistry registry)
    {
        registry.AddService<IMyService, MyService>(ServiceScope.Target);
        registry.AddService<MyHelper>(ServiceScope.Runtime);
        registry.AddProvider<IRuntimeProvider, MyRuntimeProvider>();
    }

    public void RegisterCommands(ICommandService commands)
    {
        commands.AddCommands<MyCommand>();
        commands.AddCommands<MyOtherCommand>();
    }
}
```

`Host.CreateServiceContainer` consumes a snapshot of the catalog's factories, constructs each module once per service manager, and calls `RegisterServices` before finalization. `ServiceManager.RegisterCommands` then calls `RegisterCommands` once per command service, using the same module instances and order. Modules may register services at any scope or add ordered provider factories. Their constructors do not need to be public or parameterless because factories construct them directly. A services-only or commands-only module leaves its other registration method empty. Registration and construction failures propagate to the caller.

Additional factories are sorted by assembly-qualified module name using ordinal comparison, independently of initializer execution order. Duplicate module types in the catalog are rejected. `ServiceManager.RegisterModule` registers each module type only once per manager, including when it is also advertised through the catalog. The catalog is shared within the application's load context, but module and service instances are not shared between hosts.

`IServiceManager.DiagnosticModules` exposes the successfully registered module instances in registration order. `sosstatus` displays their fully qualified type names and assembly versions, including both built-in and additional modules, without relying on assembly file locations.

Direct module registration, catalog consumption, command registration, and service finalization are public startup operations on `ServiceManager`; hosts do not require friend access. Additional assemblies contribute through `DiagnosticsModuleCatalog.Register`.

Each additional assembly must be initialized before the host consumes the catalog. Merely adding an unused project or package reference does not guarantee initialization on ordinary .NET. NativeAOT runs the included modules' initializers at startup, but the application must still include and retain each contributing assembly. A module initialized after a host's service finalization contributes only to later hosts, not existing containers. No DLLs are discovered or loaded from disk. Older target frameworks can compile-link the compatibility definition in `src/shared/ModuleInitializerAttribute.cs`. Application-internal initializers may intentionally suppress CA2255, which warns against initialization side effects in general-purpose libraries.

Constructor injection requires a public constructor. Required dependencies must be available from the container; optional dependencies can use constructor parameter defaults. The typed registration helpers preserve public constructor metadata for trimming and NativeAOT, where `ActivatorUtilities` uses its non-code-generating activation path. Services no longer use the repository's reflected constructor/member injection. Special cases, including factories that conditionally return `null` and native library initialization through `SOSLibrary.TryCreate`, use `AddServiceFactory<T>` with an explicit factory. `SOSHost` uses a provider-taking constructor to preserve its native-client initialization order.

The existing parent-container lookup, service invalidation, and disposal lifetimes are unchanged: `ActivatorUtilities` handles construction, not scope or ownership. This makes service registration and activation suitable for NativeAOT, but does not establish NativeAOT support for the entire host: command metadata, argument/option binding, and attribute-selected method invocation still use reflection on explicitly registered types, and arbitrary managed extension DLL loading is not supported by NativeAOT.

Service and provider export attributes, assembly scanning, and member-based service imports have been removed. Services and commands receive their dependencies through constructors.

The [ServiceManager](../../src/Microsoft.Diagnostics.DebugServices.Implementation/ServiceManager.cs) implements both the registration contract and [IServiceManager](../../src/Microsoft.Diagnostics.DebugServices/IServiceManager.cs). It creates [ServiceContainerFactory](../../src/Microsoft.Diagnostics.DebugServices/ServiceContainerFactory.cs) instances for the scopes, from which [ServiceContainer](../../src/Microsoft.Diagnostics.DebugServices/ServiceContainer.cs) instances are built.

### IDumpTargetFactory

This global service allows dump ITargets to be created.

See [IDumpTargetFactory.cs](../../src/Microsoft.Diagnostics.DebugServices/IDumpTargetFactory.cs) for more details.

### ICommandService

This service provides the parsing, dispatching and executing of standardized commands. It is implemented using System.Commandline but there should be no dependencies on System.CommandLine exposed to the commands or other services. It is an implementation detail.

See [ICommandService.cs](../../src/Microsoft.Diagnostics.DebugServices/ICommandService.cs)

Commands are registered in the same module file as services, through its `RegisterCommands` method:

```csharp
public void RegisterCommands(ICommandService commands)
{
    commands.AddCommands<MyCommand>();
    commands.AddCommands<MyOtherCommand>();
}
```

Add each new built-in analysis command to `AnalysisModule.RegisterCommands`, and each native SOS wrapper command type to `SOSHostingModule.RegisterCommands`. Registration order matters when command names overlap: later registrations take precedence, with the existing filters allowing fallback to earlier implementations. Attributes still define names, aliases, help, arguments, options, and filters on each registered type; module discovery does not replace this per-type metadata.

### IConsoleService

Abstracts the console output across all the platforms.

See [IConsoleService.cs](../../src/Microsoft.Diagnostics.DebugServices/IConsoleService.cs).

### IConsoleFileLoggingService

This service controls how the console service output is logged to a file.

See [IConsoleFileLoggingService.cs](../../src/Microsoft.Diagnostics.DebugServices/IConsoleFileLoggingService.cs).

### IMemoryService

Abstracts the memory related functions.

There are two helper IMemoryService implementations ImageMappingMemoryService and MetadataMappingMemoryService. They are used to wrap the base native debugger memory service implementation. 

ImageMappingMemoryService is used in dotnet-dump for Windows targets to mapping PE, ELF and MachO images like coreclr.dll, libcoreclr.so, etc. into the memory address space the module's memory isn't present. It downloads and loads the actual module and performs the necessary fix ups.

MetadataMappingMemoryService is only used for core dumps when running under lldb to map the managed assemblies metadata into address space. This is needed because the way lldb returns zero's for invalid memory for dumps generated with createdump on older runtimes (< 5.0). 

To prevent recursion in these mapping services, the target service container is cloned and the memory service being wrapped replaces the base target memory service.

The address sign extension plan for 32 bit processors (arm32/x86) is that address are masked on entry to the managed infrastructure from DAC or DBI callbacks or from the native SOS code in SOS.Hosting. If the native debugger that the infrastructure is hosted needs addresses to be signed extended like dbgeng, it will happen in the debugger services layer (IDebuggerService).

See [IMemoryService.cs](../../src/Microsoft.Diagnostics.DebugServices/IMemoryService.cs).

### IThreadService and IThread

Abstracts the hosting debuggers native threads. There are functions to enumerate, get details and context about native threads.

See [IThreadService.cs](../../src/Microsoft.Diagnostics.DebugServices/IThreadService.cs) and [IThread.cs](../../src/Microsoft.Diagnostics.DebugServices/IThread.cs)

### IModuleService and IModule

Abstracts the modules in the target. Provides the details the name, base address, build id, version, etc. Some targets this includes both native and managed modules (Windows dbgeng, Linux dotnet-dump ELF dumps) but there are hosts/targets (Linux/MacOS lldb) that only provide the native modules. 

One issues that may need to be addressed is that some platforms (MacOS) have non-contiguous memory sections in the module so the basic ImageSize isn't enough. May need (maybe only internally) need some concept of "sections" (address, size) and/or "header size".  One of the main uses of the ImageBase/ImageSize is to create a memory stream of the PE or module header to extract module details like version, build id, or search for embedded version string.

See [IModuleService.cs](../../src/Microsoft.Diagnostics.DebugServices/IModuleService.cs) and [IModule.cs](../../src/Microsoft.Diagnostics.DebugServices/IModule.cs).

### ISymbolService and ISymbolFile

This service provides the symbol store services like the functionality that the static APIs in SOS.NETCore's SymbolReader.cs does now. The SOS.NETCore assembly will be removed and replaced with this symbol service implementation. Instead of directly creating delegates to static functions in this assembly, there will be a symbol service wrapper that provides these functions to the native SOS.

The current implementation of the symbol downloading support in SOS.NETCore uses sync over async calls which could cause problems in more async hosts (like VS) but it hasn't caused problems in dotnet-dump so far. To fix this there may be work in the Microsoft.SymbolStore (in the symstore repo) component to expose synchronous APIs.

See [ISymbolService.cs](../../src/Microsoft.Diagnostics.DebugServices/ISymbolService.cs) and [ISymbolFile.cs](../../src/Microsoft.Diagnostics.DebugServices/ISymbolFile.cs)) for more details.

### IRuntimeService/IRuntimeProvider/IRuntime

This service provides the runtime instances in the target process. The IRuntimeService gathers all the runtimes from the possibility multiple IRuntimeProvider's in the system. There is a IRuntimeProvider for the runtimes found with CLRMD and will be one for the Native AOT snapshot parser. The IRuntime abstracts the runtime providing the ClrInfo and ClrRuntime instances from CLRMD and snapshot parser instance for Native AOT.

See [IRuntimeService.cs](../../src/Microsoft.Diagnostics.DebugServices/IRuntimeService.cs), [IRuntimeProvider](../../src/Microsoft.Diagnostics.DebugServices/IRuntimeProvider.cs), [IRuntime.cs](../../src/Microsoft.Diagnostics.DebugServices/IRuntime.cs) and [runtime.h](../../src/SOS/inc/runtime.h) for details.

### SOSHost/SOSLibrary

This service allows native SOS commands to be executed under hosts like dotnet-dump and VS. It provides all the native SOS hosting pinvokes and interfaces to run the native SOS commands under these debuggers. SOSLibrary is the global portion and manages loading the native SOS module and SOSHost is the per-target portion that does the actual work.

[SOSHost](../../src/SOS/SOS.Hosting/SOSHost.cs) and [SOSLibrary](../../src/SOS/SOS.Hosting/SOSLibrary.cs) for details.

### IHostServices

This interface provides services to the native SOS/plugins code. It is a private interface between the native SOS code and the SOS.Extensions host. There are services to register the IDebuggerService instance, dispatch commands and create/detroy target instance.

[hostservices.h](../../src/SOS/inc/hostservices.h) for details.

### IDebuggerService

This native interface is what the SOS.Extensions host uses to implement the above services. This is another private interface between SOS.Extensions and the native lldb plugin or Windows SOS native code.

[debuggerservice.h](../../src/SOS/inc/debuggerservices.h) for details.

## Projects and Assemblies

### SOS.Extensions

This assembly implements the host, target and services for the native debuggers (dbgeng, lldb). It provides the IHostServices to the native "extensions" library which registers the IDebuggerService used by the service implementations.

### The "extensions" native library

This is the native code that interops with the managed SOS.Extensions to host the native debuggers. It sets up the managed runtime (.NET Core on Linux/MacOS or desktop on Windows) and calls the SOS.Extensions initialization entry point. It is linked into the lldbplugin on Linux/MacOS and into SOS.dll on Windows. 

### SOS.Hosting

This contains the hosting support used by the dotnet-dump REPL and an eventual Visual Studio package to run native SOS commands without a native debugger like dbgeng or lldb.

### Microsoft.Diagnostics.DebugServices

Contains definations and abstractions for the various services interfaces. 

### Microsoft.Diagnostics.DebugServices.Implementation

Contains the common debug services implementations used by hosts like dotnet-dump and SOS.Extensions (dbgeng/lldb) hosts.

### Microsoft.Diagnostics.ExtensionCommands

Contains the common commands shared with dotnet-dump, VS and the SOS.Extensions hosts.

### Microsoft.Diagnostics.Repl

The command REPL and console service implementations. 

### dotnet-dump

The dump collection and analysis REPL global tools. It hosts the extensions layer and debug services using ClrMD's Linux and Windows minidump data readers.

### lldbplugin

The lldb plugin that provides debugger services (IDebuggerServices) to SOS.Extensions and LLDBServices to native SOS. It displays both the native SOS and new managed extension commands. It initializes the managed hosting layer via the "extensions" native library.

### Strike (SOS)

Native SOS commands and code.

On Windows, it provides the debugger services (IDebuggerServices) to SOS.Extensions and initializes the managed hosting layer via the "extensions" native library.
 
## How to write a command

Writing a new SOS command is a lot easier now that they are written C# with easy access to various services and the CLRMD API. 

The first step is to decide whether you want the new command to be part of the existing set of "built-in" commands (part of the Microsoft.Diagnostics.ExtensionCommands assembly) or in your own referenced project or NuGet package. Built-in commands must be added to `AnalysisModule.RegisterCommands`. A separate assembly advertises its `IDiagnosticsModule` factory from a module initializer and registers both services and commands in that module file; it must be initialized before host registration.

Command and service assembly must be have a netstandard2.0 TargetFramework to run on .NET Framework hosts like VS.

The next step is to create a public class that inherits from the CommandBase helper class like:

```C#
using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "clrmodules", Help = "Lists the managed modules in the process.")]
    public class ClrModulesCommand : CommandBase
    {
        public ClrModulesCommand(IServiceProvider services, IModuleService moduleService, ClrRuntime runtime = null)
            : base(services)
        {
            ModuleService = moduleService;
            Runtime = runtime;
        }

        public ClrRuntime Runtime { get; }

        public IModuleService ModuleService { get; }

        [Option(Name = "--name", Aliases = new string[] { "-n" }, Help = "RegEx filter on module name (path not included).")]
        public string ModuleName { get; set; }

        [Option(Name = "--verbose", Aliases = new string[] { "-v" }, Help = "Displays detailed information about the modules.")]
        public bool Verbose { get; set; }

        public override void Invoke()
        {
        }
    }
}
```

The "Command" attribute on the class provides the command name and help. `AddCommands<TCommand>()` registers a reference type whose execution method is marked with `CommandInvoke`. `CommandBase` supplies that attribute on its abstract `Invoke` method; commands may also declare their own attributed methods without inheriting the base class. There is no command-interface requirement or runtime-Type registration overload.

`ICommandService` exposes only `AddCommands<TCommand>(Func<IServiceProvider, TCommand>)`; the factory is required and must return a command instance. The parameterless `AddCommands<TCommand>()` extension supplies a cached `ActivatorUtilities` factory, creating a fresh instance for each invocation. Public constructor parameters declare required dependencies; parameter defaults allow optional services. `CommandBase` initializes its read-only `Services` and `Console` properties from the supplied provider.

The Option and Argument attributes define command-line binding. Only argument and option properties are set after construction, before the execution method is called. Generic registrations preserve public constructor, method, and property metadata for trimming, but command metadata, option binding, and attribute-selected method invocation still use reflection; typed registration alone does not make the command pipeline NativeAOT-compatible.

Methods marked with `CommandInvoke`, `HelpInvoke`, or `FilterInvoke` can declare service parameters resolved from the current `IServiceProvider`. An `IServiceProvider` parameter receives the current provider itself. Unavailable services use declared parameter defaults; parameters without defaults are required and produce a `DiagnosticsException` when their service is unavailable. Registered services take precedence over defaults. Callbacks must be non-generic and cannot take by-reference or pointer parameters. Static attributed methods receive injected services without creating a command, so a filter can reject an unsupported command before its required constructor dependencies are requested. Instance filters and help methods retain constructor injection and can also receive method parameters. For example:

```csharp
[FilterInvoke(Message = "No CLR runtime found.")]
public static bool FilterInvoke(ClrRuntime runtime = null) =>
    runtime != null;
```

## How to write a service

TBD

```C#
```
