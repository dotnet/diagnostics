# SOS PrintException walkthrough

This is a quick overview of how SOS and DAC work when loaded within windbg. This shows the connection between what a user does in the windbg UI, what code gets run in our tools, and what it looks like within a debugger. I am using the !PrintException command which prints information about the last managed Exception object thrown on a given thread. This is a very common command to use when an application throws an unhandled exception.

## Sample app

First we need an app to inspect under the debugger. Paste this example code into a new C# console app and build it:

```C#
namespace ExceptionExample
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Method2();
        }

        static void Method2()
        {
            try
            {
                Method3();
            }
            catch (Exception ex)
            {
                throw new AggregateException(ex);
            }
        }

        static void Method3()
        {
            throw new FormatException();
        }
    }
}
```

You can see this app will throw a FormatException from Method3, then Method2 will catch that exception and throw a new AggregateException that goes unhandled.

## Running the app in windbg

When running the app under windbg first windbg will stop at the entrypoint breakpoint. At this point we haven't yet run Main() or thrown any of the Exceptions. The ouput in windbg's console should look similar to this:

```
Microsoft (R) Windows Debugger Version 10.0.25964.1000 AMD64
Copyright (c) Microsoft Corporation. All rights reserved.

CommandLine: C:\Users\noahfalk\source\repos\ExceptionExample\ExceptionExample\bin\Debug\net8.0\ExceptionExample.exe

************* Path validation summary **************
Response                         Time (ms)     Location
Deferred                                       srv*
Symbol search path is: srv*
Executable search path is: 
ModLoad: 00007ff7`52e50000 00007ff7`52e79000   apphost.exe
ModLoad: 00007ff8`85350000 00007ff8`85567000   ntdll.dll
ModLoad: 00007ff8`84760000 00007ff8`84824000   C:\WINDOWS\System32\KERNEL32.DLL
ModLoad: 00007ff8`828e0000 00007ff8`82c86000   C:\WINDOWS\System32\KERNELBASE.dll
ModLoad: 00007ff8`84330000 00007ff8`844de000   C:\WINDOWS\System32\USER32.dll
ModLoad: 00007ff8`82fc0000 00007ff8`82fe6000   C:\WINDOWS\System32\win32u.dll
ModLoad: 00007ff8`83d00000 00007ff8`83d29000   C:\WINDOWS\System32\GDI32.dll
ModLoad: 00007ff8`82ea0000 00007ff8`82fb8000   C:\WINDOWS\System32\gdi32full.dll
ModLoad: 00007ff8`82c90000 00007ff8`82d2a000   C:\WINDOWS\System32\msvcp_win.dll
ModLoad: 00007ff8`82690000 00007ff8`827a1000   C:\WINDOWS\System32\ucrtbase.dll
ModLoad: 00007ff8`84ab0000 00007ff8`8530a000   C:\WINDOWS\System32\SHELL32.dll
ModLoad: 00007ff8`84270000 00007ff8`84323000   C:\WINDOWS\System32\ADVAPI32.dll
ModLoad: 00007ff8`841c0000 00007ff8`84267000   C:\WINDOWS\System32\msvcrt.dll
ModLoad: 00007ff8`83a60000 00007ff8`83b08000   C:\WINDOWS\System32\sechost.dll
ModLoad: 00007ff8`83070000 00007ff8`83098000   C:\WINDOWS\System32\bcrypt.dll
ModLoad: 00007ff8`831f0000 00007ff8`83307000   C:\WINDOWS\System32\RPCRT4.dll
(5fc4.6320): Break instruction exception - code 80000003 (first chance)
ntdll!LdrpDoDebuggerBreak+0x30:
00007ff8`8542b784 cc              int     3
0:000>
```

We can continue by using the 'g' (go) command, or clicking the continue button in the UI. When code execution resumes Main() will run and our exceptions will be thrown. The windbg output now looks like this:

```
0:000> g
ModLoad: 00007ff8`846c0000 00007ff8`846f1000   C:\WINDOWS\System32\IMM32.DLL
ModLoad: 00007ff8`6b9f0000 00007ff8`6ba49000   C:\Program Files\dotnet\host\fxr\8.0.2\hostfxr.dll
ModLoad: 00007ff8`60940000 00007ff8`609a4000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.2\hostpolicy.dll
ModLoad: 00007ff8`09c40000 00007ff8`0a128000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.2\coreclr.dll
ModLoad: 00007ff8`83b60000 00007ff8`83d00000   C:\WINDOWS\System32\ole32.dll
ModLoad: 00007ff8`83650000 00007ff8`839d9000   C:\WINDOWS\System32\combase.dll
ModLoad: 00007ff8`845e0000 00007ff8`846b7000   C:\WINDOWS\System32\OLEAUT32.dll
ModLoad: 00007ff8`82ff0000 00007ff8`8306a000   C:\WINDOWS\System32\bcryptPrimitives.dll
(5fc4.6320): Unknown exception - code 04242420 (first chance)
ModLoad: 00007fff`d2b30000 00007fff`d37bc000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.2\System.Private.CoreLib.dll
ModLoad: 00007ff8`0ecc0000 00007ff8`0ee79000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.2\clrjit.dll
ModLoad: 00007ff8`81590000 00007ff8`815a8000   C:\WINDOWS\SYSTEM32\kernel.appcore.dll
ModLoad: 00000197`e52d0000 00000197`e52d8000   C:\Users\noahfalk\source\repos\ExceptionExample\ExceptionExample\bin\Debug\net8.0\ExceptionExample.dll
ModLoad: 00000197`e5300000 00000197`e530e000   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.2\System.Runtime.dll
ModLoad: 00007ff8`6c680000 00007ff8`6c8f0000   C:\WINDOWS\SYSTEM32\icu.dll
(5fc4.6320): CLR exception - code e0434352 (first chance)
(5fc4.6320): CLR exception - code e0434352 (first chance)
CLR exception type: System.FormatException
    "One of the identified items was in an invalid format."
(5fc4.6320): CLR exception - code e0434352 (!!! second chance !!!)
CLR exception type: System.AggregateException
    "One or more errors occurred."
KERNELBASE!RaiseException+0x6c:
00007ff8`8294567c 0f1f440000      nop     dword ptr [rax+rax]
```

The ".chain" command shows extensions that are loaded and we can see that windbg has already automatically loaded the sos.dll extension. The sos.dll comes from windbg's extension gallery which includes
a copy of sos.dll that the .NET team periodically builds and provides to them. This dll is not tightly versioned to a particular coreclr build. Windbg always use the latest one we give them with expectations that it is compatible with a wide range of CoreCLR and .NET Framework runtime versions.

```
0:000> .chain
Extension DLL search Path:
    C:\debuggers\amd64\WINXP;C:\debuggers\amd64\winext;C:\debuggers\amd64\winext\arcade;C:\debuggers\amd64\pri;C:\debuggers\amd64;C:\Users\noahfalk\AppData\Local\Dbg\EngineExtensions;C:\debuggers\amd64;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0\;C:\WINDOWS\System32\OpenSSH\;C:\Program Files\Microsoft SQL Server\150\Tools\Binn\;C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\;C:\Program Files\dotnet\;C:\Program Files\Git\cmd;C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\;C:\Program Files (x86)\GitExtensions\;C:\Program Files\Docker\Docker\resources\bin;C:\Users\noahfalk\AppData\Local\Programs\Python\Python312\Scripts\;C:\Users\noahfalk\AppData\Local\Programs\Python\Python312\;C:\Users\noahfalk\AppData\Local\Programs\Python\Launcher\;C:\Users\noahfalk\AppData\Local\Microsoft\WindowsApps;C:\Users\noahfalk\.dotnet\tools;C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.37.32822\bin\Hostx86\x86;C:\Users\noahfalk\AppData\Local\Programs\Microsoft VS Code\bin
Extension DLL chain:
    CLRComposition: image 10.0.25964.1000, API 0.0.0, 
        [path: C:\debuggers\amd64\winext\CLRComposition.dll]
    sos: image 8.0.510501, API 2.0.0, built Mon Feb  5 14:03:41 2024
        [path: C:\Users\noahfalk\AppData\Local\DBG\ExtRepository\EG\cache3\Packages\SOS\8.0.10.10501\win-x64\sos.dll]
    dbghelp: image 10.0.25964.1000, API 10.0.6, 
        [path: C:\debuggers\amd64\dbghelp.dll]
    exts: image 10.0.25964.1000, API 1.0.0, 
        [path: C:\debuggers\amd64\WINXP\exts.dll]
    uext: image 10.0.25964.1000, API 1.0.0, 
        [path: C:\debuggers\amd64\winext\uext.dll]
    ntsdexts: image 10.0.25964.1000, API 1.0.0, 
        [path: C:\debuggers\amd64\WINXP\ntsdexts.dll]
```

If we wanted to we could use the ".unload" and ".load" commands to unload the version of sos.dll that windbg automatically loaded and then load a different one. This can be useful for testing a local development version of SOS rather than the already released version. For this demo we'll stick with the released version.

## Debugging SOS running within windbg

Now we are going to launch a 2nd debugger and attach it to the windbg process. To help keep the debuggers distinguished I am attaching Visual Studio but a 2nd instance of windbg would also work. The current setup is now Visual Studio debugging windbg which is in turn debugging our .NET app ExceptionExample.exe.

### Getting source and symbols

Using the path we saw in the ".chain" command above we know sos.dll was loaded from C:\Users\noahfalk\AppData\Local\DBG\ExtRepository\EG\cache3\Packages\SOS\8.0.10.10501\win-x64\sos.dll. We can open the properties on this file and look in the Details tab to see the Product Version is 8.0.510501. We can use this to download the proper source from GitHub. Beware there is also a File version (8.0.10.10501) that is similar but not identical. We want Product Version, not File version.

I have cloned https://github.com/dotnet/diagnostics to a local repo at C:\git\diagnostics. The dotnet/diagnostics repo is set as my upstream remote:

```
C:\git\diagnostics>git remote -v
origin  https://github.com/noahfalk/diagnostics (fetch)
origin  https://github.com/noahfalk/diagnostics (push)
upstream        https://github.com/dotnet/diagnostics (fetch)
upstream        https://github.com/dotnet/diagnostics (push)
```

I can download all the tags from upstream:
```
C:\git\diagnostics>git fetch upstream --tags
remote: Enumerating objects: 1228, done.
remote: Counting objects: 100% (949/949), done.
remote: Compressing objects: 100% (128/128), done.

... (lots of other output omitted)

 * [new tag]           v7.0.447801            -> v7.0.447801
 * [new tag]           v8.0.452401            -> v8.0.452401
 * [new tag]           v8.0.505301            -> v8.0.505301
 * [new tag]           v8.0.510501            -> v8.0.510501
```

Then I can checkout the v8.0.510501 tag that matches this build:

```
C:\git\diagnostics>git checkout v8.0.510501
Note: switching to 'v8.0.510501'.

You are in 'detached HEAD' state. You can look around, make experimental
changes and commit them, and you can discard any commits you make in this
state without impacting any branches by switching back to a branch.

If you want to create a new branch to retain commits you create, you may
do so (now or later) by using -c with the switch command. Example:

  git switch -c <new-branch-name>

Or undo this operation with:

  git switch -

Turn off this advice by setting config variable advice.detachedHead to false

HEAD is now at 8c08c89a Fix dotnet-trace rundown default (#4498)
```

In the Visual Studio module window I see the sos.dll is loaded as well as symbol loading status:


| Name | Path | Optimized | User Code | Symbol Status | Symbol File | Order | Version | Timestamp | Address | Process |
|------|------|-----------|-----------|---------------|-------------|-------|---------|-----------|---------|---------|
sos.dll |	sos.dll |	C:\Users\noahfalk\AppData\Local\DBG\ExtRepository\EG\cache3\Packages\SOS\8.0.10.10501\win-x64\sos.dll |	N/A |	Yes |	Cannot find or open the PDB file. |		111	| 8.00.10.10501 |	2/5/2024 2:03 PM |	00007FFFD53E0000-00007FFFD560A000 |	[8244] windbg.exe		

Make sure that 'Microsoft Symbol Servers' is enabled in VS symbol options and then right click on sos.dll in the Modules window and select 'Load Symbols'. The symbols for sos.dll should be found and the UI updates indicating symbols are loaded. If you are using a different debugger than VS, the Microsoft public symbol server is https://msdl.microsoft.com/download/symbols.

### Setting an initial breakpoint

From the git repo we just synced, open the file src\SOS\Strike\strike.cpp in Visual Studio. Search in the file for a line that looks like `DECLARE_API(PrintException)`. This DECLARE_API macro defines an exported function that gets invoked by windbg when running a command. The !PrintException command will invoke the PrintException exported function. Set a breakpoint on the first line of code in this funtion. At the moment it is this macro INIT_API_PROBE_MANAGED().

```
DECLARE_API(PrintException)
{
    INIT_API_PROBE_MANAGED("printexception");

    BOOL dml = FALSE;
    BOOL bShowNested = FALSE;
    BOOL bLineNumbers = FALSE;
```

Once the breakpoint is placed type '!PrintException' into the windbg console, hit enter, and the breakpoint will be hit when windbg starts running the command. I don't have symbols or source for windbg so the callstack looks like this:

```
sos.dll!PrintException(IDebugClient * client, const char * args) Line 2848
dbgeng.dll!00007fffd5ca65f6()
dbgeng.dll!00007fffd5ca62b5()
dbgeng.dll!00007fffd5ca68a5()
dbgeng.dll!00007fffd5cafb9c()
...
```

### Exploring some SOS initialization code

The PrintException function signature `(IDebugClient* client, const char* args)` is a standard extension invocation supported by windbg. The IDebugClient is the interface for extension code to interact with windbg's state and the args parameter provides text arguments the developer wrote after the '!PrintException' part of the commandline. It is entirely up to the PrintException function how to interpret any args that are present.

The first thing PrintException does is invoke the macro `INIT_API_PROBE_MANAGED()` which is defined in exts.h and expands out into several more macros:
```
#define INIT_API_PROBE_MANAGED(name)                            \
    INIT_API_NODAC_PROBE_MANAGED(name)                          \
    INIT_API_DAC()

#define INIT_API_NODAC_PROBE_MANAGED(name)                      \
    INIT_API_NOEE_PROBE_MANAGED(name)                           \
    INIT_API_EE()

#define INIT_API_NOEE_PROBE_MANAGED(name)                       \
    INIT_API_EXT()                                              \
    if ((Status = ExecuteCommand(name, args)) != E_NOTIMPL) return Status; \
    if ((Status = ArchQuery()) != S_OK) return Status;
```

A lot of initialization steps are happening in this macro:

1. INIT_API_EXT() is initializing a bunch of global variables with names g_Ext* to point at various COM interfaces that can be retrieved from the IDebugClient. Rather than pass the IDebugClient around and QI on demand SOS does it up-front at the start of every command.
2. ExecuteCommand(name, args) is a hook that allows SOS's default C++ command implementations to be replaced by an optional alternative C# implementation loaded from another assembly. !PrintException won't have an alternative implementation in this scenario but there are also initialization side effects that happen during that search.

  - SOS attempts to spin up some .NET host, either desktop .NET or CoreCLR, so that it can bootstrap the managed portion of the SOS implementation:

```
>	SOS.Extensions.dll!SOS.Extensions.HostServices.Initialize(string extensionPath) Line 86	C#
 	[Native to Managed Transition]	
 	sos.dll!InitializeNetCoreHost() Line 667	C++
 	[Inline Frame] sos.dll!InitializeHosting() Line 749	C++
 	sos.dll!SOSExtensions::GetHost() Line 450	C++
 	[Inline Frame] sos.dll!Extensions::GetHostServices() Line 118	C++
 	[Inline Frame] sos.dll!GetHostServices() Line 144	C++
 	[Inline Frame] sos.dll!ExecuteCommand(const char *) Line 209	C++
 	sos.dll!PrintException(IDebugClient * client, const char * args) Line 2848	C++
```

  - The managed code p/invokes back to SOS to register various COM interfaces that the C++ SOS will be able to use later to access functionality that was written in managed code:

```
 	[Inline Frame] sos.dll!Extensions::InitializeHostServices(IUnknown *) Line 99	C++
>	sos.dll!InitializeHostServices(IUnknown * punk) Line 28	C++
 	[Managed to Native Transition]	
 	SOS.Extensions.dll!SOS.Extensions.HostServices.Initialize(string extensionPath) Line 106	C#
 	[Native to Managed Transition]	
 	sos.dll!InitializeNetCoreHost() Line 667	C++
 	[Inline Frame] sos.dll!InitializeHosting() Line 749	C++
 	sos.dll!SOSExtensions::GetHost() Line 450	C++
 	[Inline Frame] sos.dll!Extensions::GetHostServices() Line 118	C++
 	[Inline Frame] sos.dll!GetHostServices() Line 144	C++
 	[Inline Frame] sos.dll!ExecuteCommand(const char *) Line 209	C++
 	sos.dll!PrintException(IDebugClient * client, const char * args) Line 2848	C++
```

  - After all the managed code initialization we ask the managed code portion of SOS if it has an alternate implementation of PrintException to run and it doesn't. It returns E_NOTIMPL back to native code to signal this:

```
>	SOS.Extensions.dll!SOS.Extensions.HostServices.DispatchCommand(nint self, string commandName, string commandArguments) Line 363	C#
 	[Native to Managed Transition]	
 	[Inline Frame] sos.dll!ExecuteCommand(const char *) Line 212	C++
 	sos.dll!PrintException(IDebugClient * client, const char * args) Line 2848	C++
```

3. After unwinding out of all the C# intitialization ArchQuery() uses windbg interfaces to request the hardware architecture of the debuggee and returns an error if the architecture is unsupported.
4. INIT_API_EE() initializes the g_pRuntime field. This interface encapsulates SOS's knowledge about what .NET runtime it is debugging and where to find corresponding debug binaries like mscordaccore and mscordbi for this runtime.
5. INIT_API_DAC() initializes the g_clrData field. This interface is SOS's entrypoint into DAC APIs from which it will QI all other DAC interfaces that it uses. This step also initializes the g_sos field with a reference to DAC's ISOSDacInterface. 

```
>	mscordaccore.dll!CLRDataCreateInstance(const _GUID & iid, ICLRDataTarget * pLegacyTarget, void * * iface) Line 7109	C++
 	[Managed to Native Transition]	
 	SOS.Hosting.dll!SOS.Hosting.RuntimeWrapper.CreateClrDataProcess() Line 313	C#
 	SOS.Hosting.dll!SOS.Hosting.RuntimeWrapper.GetClrDataProcess(nint self, nint* ppClrDataProcess) Line 213	C#
 	[Native to Managed Transition]	
 	sos.dll!LoadClrDebugDll() Line 3840	C++
 	sos.dll!PrintException(IDebugClient * client, const char * args) Line 2848	C++
```

### PrintException command logic

After initialization, PrintException() starts running various parts of the command logic:

#### Argument parsing helpers
```
CMDOption option[] =
{   // name, vptr, type, hasValue
    {"-nested", &bShowNested, COBOOL, FALSE},
    {"-lines", &bLineNumbers, COBOOL, FALSE},
    {"-l", &bLineNumbers, COBOOL, FALSE},
    {"-ccw", &bCCW, COBOOL, FALSE},
    {"/d", &dml, COBOOL, FALSE}
};
CMDValue arg[] =
{   // vptr, type
    {&strObject, COSTRING}
};
size_t nArg;
if (!GetCMDOption(args, option, ARRAY_SIZE(option), arg, ARRAY_SIZE(arg), &nArg))
{
    return E_INVALIDARG;
}
```

#### Breaking change detection

We put a hard-coded breaking change number in the CoreCLR source. At debug time SOS extracts this number and compares it with the number embedded in SOS at build time. If CoreCLR has a higher number then SOS prints an error message saying that SOS is too old and the user should seek out a new version. In order to retrieve CoreCLR's version we use DAC to fetch this value and I believe it is first usage of the DAC APIs after initialization.

```
>	mscordaccore.dll!ClrDataAccess::GetBreakingChangeVersion(int * pVersion) Line 4950	C++
 	sos.dll!CheckBreakingRuntimeChange(int * pVersion) Line 3287	C++
 	sos.dll!PrintException(IDebugClient * client, const char * args) Line 2875	C++
```

The DAC side source is extremely simple for this API (src\coreclr\debug\daccess\request.cpp in the runtime repo):
```
HRESULT ClrDataAccess::GetBreakingChangeVersion(int* pVersion)
{
    if (pVersion == nullptr)
        return E_INVALIDARG;

    *pVersion = SOS_BREAKING_CHANGE_VERSION;
    return S_OK;
}
```

Notice that DAC didn't actually read anything from the debuggee memory space, it just returned a hard-coded number. This only works as long as DAC and CoreCLR are always versioned together tightly. As soon as we allow DAC to version independently then we must define where this breaking change number will exist in the debuggee memory and read it from there.

#### Current Managed thread

Skipping down a bit in PrintException() code past some more configuration checks SOS determines the address of the coreclr!Thread object that corresponds to the current thread selected in the windbg UI:
```
        // Look at the last exception object on this thread

        CLRDATA_ADDRESS threadAddr = GetCurrentManagedThread();
```

In the implementation of GetCurrentManagedThread() first SOS asks DAC for information about the ThreadStore: 
```
>	mscordaccore.dll!ClrDataAccess::GetThreadStoreData(DacpThreadStoreData * threadStoreData) Line 299	C++
 	[Inline Frame] sos.dll!DacpThreadStoreData::Request(ISOSDacInterface * sos) Line 323	C++
 	sos.dll!GetCurrentManagedThread() Line 3113	C++
 	sos.dll!PrintException(IDebugClient * client, const char * args) Line 2904	C++

HRESULT ClrDataAccess::GetThreadStoreData(struct DacpThreadStoreData *threadStoreData)
{
    SOSDacEnter();

    ThreadStore* threadStore = ThreadStore::s_pThreadStore;
    if (!threadStore)
    {
        hr = E_UNEXPECTED;
    }
    else
    {
        // initialize the fields of our local structure
        threadStoreData->threadCount = threadStore->m_ThreadCount;
        threadStoreData->unstartedThreadCount = threadStore->m_UnstartedThreadCount;
        threadStoreData->backgroundThreadCount = threadStore->m_BackgroundThreadCount;
        threadStoreData->pendingThreadCount = threadStore->m_PendingThreadCount;
        threadStoreData->deadThreadCount = threadStore->m_DeadThreadCount;
        threadStoreData->fHostConfig = FALSE;

        // identify the "important" threads
        threadStoreData->firstThread = HOST_CDADDR(threadStore->m_ThreadList.GetHead());
        threadStoreData->finalizerThread = HOST_CDADDR(g_pFinalizerThread);
        threadStoreData->gcThread = HOST_CDADDR(g_pSuspensionThread);
    }

    SOSDacLeave();
    return hr;
}
```

The line that assigns `ThreadStore* threadStore = ThreadStore::s_pThreadStore;` looks deceptively simple but this is actually doing some non-trivial marshalling of data from the debuggee memory space into the debugger memory space. ThreadStore::s_pThreadStore is defined as type PTR_ThreadStore which is a typedef for a smart pointer when built in the DAC binary. The assignment from smart pointer to ThreadStore* invokes the implicit casting operator of the smart pointer. That casting operator is calling `DacInstantiateTypeByAddress(remote_address, sizeof(ThreadStore), true)` to copy the memory that backs the ThreadStore object. See the giant comments in https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/daccess.h if you want to dig deeper into how the code discovered the remote address of the s_pThreadStore, how it accesses debuggee memory, and all the other intricacies of the smart pointer implementation. 

One important thing to call out is that the `sizeof(ThreadStore)` which determines the size of the memory copy is computed from mscordaccore!ThreadStore, not coreclr!ThreadStore. The current DAC implementation depends on having access to type definitions that are identical to the ones in CoreCLR, and that they compiler determines them to have an identical layout. If DAC was given a different C++ type definition or the compiler infered a different layout then DAC would not know the correct size of a ThreadStore object to copy from memory.

Once DAC has a local copy of the ThreadStore object it reads various fields from it and stores them in the DacpThreadStoreData structure which gets returned to the caller. The DacpThreadStoreData structure is intended to be a version independent definition of the interesting fields of a ThreadStore. If for example the runtime changed `LONG m_UnstartedThreadCount` to `BYTE m_newThreadCount` the DAC code would need to be updated here but SOS would be insulated from that change. However DacpThreadStoreData clearly still embeds some knowledge about the kind of data stored in a ThreadStore. If a larger change completely eliminated the ThreadStore or radically redesigned it then the interface between DAC and SOS would also need to change. This is a case where we would bump the SOS breaking change version number so that the old implementation of SOS doesn't attempt to introspect on runtime data structures it won't understand.


After getting the ThreadStore information SOS asks windbg for the current thread ID and then searches the ThreadStore for a Thread object that matches that ID:
```
ULONG Tid;
g_ExtSystem->GetCurrentThreadSystemId(&Tid);

CLRDATA_ADDRESS CurThread = ThreadStore.firstThread;
while (CurThread)
{
    DacpThreadData Thread;
    if (Thread.Request(g_sos, CurThread) != S_OK)
    {
        return NULL;
    }

    if (Thread.osThreadId == Tid)
    {
        return CurThread;
    }

    CurThread = Thread.nextThread;
}
```

The Thread.Request(g_sos, CurThread) is another call into the DAC, this time fetching information about coreclr!Thread objects based on their address.

#### Exception information

After getting the current thread PrintException gets the OBJECTHANDLE for the last thrown exception, reads the Exception pointer out of the handle, gets information about the Exception object, and formats that data to print it back to Windbg's console with the ExtOut() function. Part of those steps are abstracted with further DAC calls but sometimes SOS gets fairly deep in the details itself. For example to convert the `OBJECTHANDLE` to an `Exception*` SOS assumes that `OBJECTHANDLE` is an `Exception**` and directly reads the first pointer sized value from the debuggee memory at that address using the SafeReadMemory() function. Ideally SOS wouldn't do that but historically there has never been a forcing function to prevent these occasional bits of low-level type layout assumptions from leaking into SOS's implementation.

A quick rundown of some of the remaining SOS->DAC interactions in PrintException:

- If the exception-object-address is 0, get the exception object from the managed current thread
    - Call ISOSDacInterface::GetThreadStoreData to get the first thread
    - Call ISOSDacInterface::GetThreadData until the current thread is found
    - Read the exception-object-address at the DacpThreadData.lastThrownObjectHandle address
- Call ISOSDacInstance::GetObjectData(<exception-object-address>) fills in a DacpObjectData struct about the object.
- Check if object method table (DacpObjectData.MethodTable) is Exception or derived from Exception
    - Call ISOSDacInterface::GetUsefulGlobals() to get System.Exception MethodTable address.
    - Call ISOSDacInterface::GetMethodTableData() to get the parent method table (DacpMethodTableData.ParentMethodTable).
- Gets the exception type name from the object method table
    - Call ISOSDacInterface::GetMethodTableName
- Call ISOSDacInterface2::GetObjectExceptionData to get the basic exception info (DacpExceptionObjectData).
- Generate the exception stack trace from the DacpExceptionObjectData.StackTrace
    - Get the stack trace array size and start address with hard coded assumptions from the runtime's clrex.h.
    - Format the stack trace array. This is where SOS has direct knowledge about the StackTraceElement array from the Exception object. It makes assumptions about the layout and IP adjustment. All of this should ideally be in a DAC API.
        - Call ISOSDacInterface::GetMethodDescData on the MethodDesc from StackTraceElement (pFunc).
        - Call ISOSDacInterface::GetModuleData on the module pointer in the MD.
        - Call ISOSDacInterface::GetPEFileBase on the module's PEAssembly. Returns the module base address.
        - Get the module name/path from the native debugger from the module base address.
        - If that fails, use ISOSDacInterface::GetPEFileName to get the module name.
        - If that fails, use ISOSDacInterface::GetModule to get the IXCLRDataModule instance
            - IXCLRDataModule::GetFileName to get the module name
        - Call ISOSDacInterface::GetMethodDescName on the MD address to get the method name
    - Display the source/line number info for this stack trace.

