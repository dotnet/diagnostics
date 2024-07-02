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

### References

SOS CLRMA export code: https://github.com/dotnet/diagnostics/blob/main/src/SOS/Strike/clrma/clrma.cpp. 

SOS CLRMA wrapper code: https://github.com/dotnet/diagnostics/blob/main/src/SOS/SOS.Extensions/Clrma/ClrmaServiceWrapper.cs. 
