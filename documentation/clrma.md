## SOS Watson/!analyze support

This document summarizes what functionality is needed by Watson/!analyze to properly bucket crash dumps. The audience is the runtime devs working on the cDAC. Historically !analyze wrapped calls to SOS with an internal interface called CLRMA. This set of interfaces abstracted how !analyze obtained the crash information like managed threads, thread stack traces, managed exception and exception stack traces, etc. from SOS on a crash dump. Now the CLRMA interfaces are a public contract between SOS and !analyze. 

CLRMA interface definition: https://github.com/dotnet/diagnostics/blob/main/src/SOS/inc/clrma.idl

### CLRMA interfaces

SOS now exposes new exports (CLRMACreateInstance/CLRMAReleaseInstance) that !analyze looks for to create the top level CLRMA instance (ICLRManagedAnalysis). SOS supports Native AOT runtime crashes via the crash info JSON blob in a runtime global buffer and other .NET runtimes with the direct DAC support under the "SOS/clrma" directory.

#### ICLRManagedAnalysis

This root interface contains some setup functions like ProviderName/AssociateClient and also functions to get the thread (GetThread) and exception (GetException) interface instances. This implementation calls the managed clrma service first to see if there is Native AOT crash info, otherwise, it enables the direct DAC support.

#### ICLRMAClrThread

Provides the details about a specific or the current managed thread like managed stack trace and the current exceptions. If the stack trace isn't implemented (as in the Native AOT case), !analyze uses the native stack trace for the bucketing.

#### ICLRMAClrException

Provides all the details about a specific or current thread's managed exception like the type, message string, stack trace and inner exceptions.

#### ICLRMAObjectInspection

The object inspection interface is a set of optional functions to get an object's type or field's value. It used to get more detailed information about an exception object like the file path field from a FileNotFoundException. They are used to refine the bucketing for exception and other types.

Here are some examples of !analyze object field inspection (this is not exhaustive list):

- FileNotFoundException: _fileName, _fusionLog
- BadImageFormatException._fileName
- Exception._remoteStackTraceString
- IOException._maybeFullPath
- SocketException.nativeErrorCode
- TypeInitializationException._typeName
- TypeLoadException: ClassName, AssemblyName
- WebException: m_Response.m_Uri.m_String, m_Response.m_StatusDescription, m_Response.m_StatusCode

Not implemented at this time for Native AOT or .NET Core.

### DAC interfaces used by CLRMA

```
ISOSDacInterface::GetUsefulGlobals()
ISOSDacInterface::GetThreadStoreData()
ISOSDacInterface::GetThreadData()
ISOSDacInterface::GetNestedExceptionData()
ISOSDacInterface::GetObjectData()
ISOSDacInterface2::GetObjectExceptionData()
ISOSDacInterface::GetMethodTableName()
ISOSDacInterface::GetMethodTableData()
ISOSDacInterface::GetObjectStringData()
ISOSDacInterface::GetMethodDescData()
ISOSDacInterface::GetMethodDescName()
ISOSDacInterface::GetModuleData()
ISOSDacInterface::GetPEFileBase()
ISOSDacInterface::GetPEFileName()
ISOSDacInterface::GetMethodDescPtrFromIP()

// Module name fallback if debugger and GetPEFileName() can't get the name.
ISOSDacInterface::GetModule()
IXCLRDataModule::GetFileName 

// Managed stack walking
IXCLRDataProcess::GetTaskByOSThreadID()
IXCLRDataTask::CreateStackWalk()
IXCLRDataStackWalk::Request(DACSTACKPRIV_REQUEST_FRAME_DATA, ...)
IXCLRDataStackWalk::GetContext()
IXCLRDataStackWalk::Next()
```

### Testing CLRMA locally with the `clrma` command

> **Note:** `clrma` is an internal diagnostic/validation command used to exercise the CLRMA path.
> It is intentionally hidden from the `help`/`soshelp` command list and is not a supported,
> publicly advertised triage command. It is still invocable by name in any SOS host.

The `clrma` SOS command exercises the CLRMA contract end-to-end (`CLRMACreateInstance` →
`ICLRManagedAnalysis.AssociateClient` → `GetThread`/`GetException`) and prints the managed thread
stack, the current/nested exceptions, their types, messages, HResults and exception stack traces.
This is the same data Watson/`!analyze` extracts from CLRMA, so the command is a local proxy for
"upload to Watson and see how it buckets" — without needing the debugger engine's `!clrma`/`!analyze`.

The command runs in any SOS host. In particular it works under `dotnet-dump`, so no Windows
debugger (windbg/cdb) or lldb is required:

```
dotnet-dump analyze <dump> -c "clrma" -c "exit"
```

By default it analyzes the current/faulting thread and its current exception. Pass `-t <osThreadId>`
to target a specific managed thread. Use `clrmaconfig` to switch between the managed (Native AOT
crash-info) provider and the direct-DAC provider, e.g. `clrmaconfig -enable -dac`.

To produce a dump to test against, run a managed app under `createdump` (no debugger needed):

```
set DOTNET_DbgEnableMiniDump=1
set DOTNET_DbgMiniDumpType=4
set DOTNET_DbgMiniDumpName=<path-to-dump>
<run the crashing app>
```

Example output for an `InvalidOperationException` that wraps an inner `ArgumentException`:

```
Managed analysis provider: SOSCLRMA
OSThreadId: 800c
Managed stack trace:
    ... clrmatest.dll!Program.Main()+0x8b
    ... clrmatest.dll!Program.Inner()+0x67
Current exception:
    Exception type:   System.InvalidOperationException
    Message:          Outer failure from CLRMA test debuggee
    HResult:          80131509
    StackTrace (generated):
        ... clrmatest.dll!Program.Main+0x8a
    InnerException:
        Exception type:   System.ArgumentException
        Message:          Inner failure: bad argument value
        HResult:          80070057
```

> Note: under `dotnet-dump` the `Extensions` debugger-services layer is not registered (SOS uses the
> legacy dbgeng-compat layer there), so CLRMA `-logging` output is suppressed in that host.

### References

SOS CLRMA export code: https://github.com/dotnet/diagnostics/blob/main/src/SOS/Strike/clrma/clrma.cpp.

SOS CLRMA wrapper code: https://github.com/dotnet/diagnostics/blob/main/src/SOS/SOS.Extensions/Clrma/ClrmaServiceWrapper.cs. 
