// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include <cstdarg>
#include <cstdlib>
#include <string.h>
#include <string>
#include <dlfcn.h>
#if defined(__APPLE__)
#include <mach-o/loader.h>
#endif
#include "sosplugin.h"
#include "arrayholder.h"

#define CONVERT_FROM_SIGN_EXTENDED(offset) ((ULONG_PTR)(offset))

// Return what dbgeng returns for Linux modules that don't have a timestamp/checksum
#define InvalidTimeStamp    0xFFFFFFFE;
#define InvalidChecksum     0xFFFFFFFF;

char *g_coreclrDirectory = nullptr;
char *g_pluginModuleDirectory = nullptr;

LLDBServices::LLDBServices(lldb::SBDebugger debugger) :
    m_ref(1),
    m_debugger(debugger),
    m_interpreter(debugger.GetCommandInterpreter()),
    m_currentProcess(nullptr),
    m_currentThread(nullptr),
    m_currentStopId(0),
    m_processId(0),
    m_threadInfoInitialized(false)
{
    ClearCache();

    lldb::SBProcess process = GetCurrentProcess();
    if (process.IsValid())
    {
        m_currentStopId = process.GetStopID();
    }
}

LLDBServices::~LLDBServices()
{
}

//----------------------------------------------------------------------------
// IUnknown
//----------------------------------------------------------------------------

HRESULT
LLDBServices::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (InterfaceId == __uuidof(IUnknown) ||
        InterfaceId == __uuidof(ILLDBServices))
    {
        *Interface = static_cast<ILLDBServices*>(this);
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == __uuidof(ILLDBServices2))
    {
        *Interface = static_cast<ILLDBServices2*>(this);
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == __uuidof(IDebuggerServices))
    {
        *Interface = static_cast<IDebuggerServices*>(this);
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = NULL;
        return E_NOINTERFACE;
    }
}

ULONG
LLDBServices::AddRef()
{
    LONG ref = InterlockedIncrement(&m_ref);    
    return ref;
}

ULONG
LLDBServices::Release()
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

//----------------------------------------------------------------------------
// ILLDBServices
//----------------------------------------------------------------------------

PCSTR
LLDBServices::GetCoreClrDirectory()
{
    if (g_coreclrDirectory == nullptr)
    {
        lldb::SBTarget target = m_debugger.GetSelectedTarget();
        if (target.IsValid())
        {
            const char *coreclrModule = MAKEDLLNAME_A("coreclr");
            lldb::SBFileSpec fileSpec;
            fileSpec.SetFilename(coreclrModule);

            lldb::SBModule module = target.FindModule(fileSpec);
            if (module.IsValid())
            {
                const char *directory = module.GetFileSpec().GetDirectory();
                std::string path(directory);
                path.append("/");

                g_coreclrDirectory = strdup(path.c_str());
            }
        }
    }
    return g_coreclrDirectory;
}

ULONG64
LLDBServices::GetExpression(
    PCSTR exp)
{
    if (exp == nullptr)
    {
        return 0;
    }

    lldb::SBFrame frame = GetCurrentFrame();
    if (!frame.IsValid())
    {
        return 0;
    }

    ULONG64 result = 0;
    lldb::SBError error;
    std::string str;

    // To be compatible with windbg/dbgeng, we need to emulate the default
    // hex radix (because sos prints addresses and other hex values without
    // the 0x) by first prepending 0x and if that fails use the actual
    // undecorated expression.
    str.append("0x");
    str.append(exp);

    result = GetExpression(frame, error, str.c_str());
    if (error.Fail())
    {
        result = GetExpression(frame, error, exp);
    }

    return result;
}

// Internal function
ULONG64
LLDBServices::GetExpression(
    /* const */ lldb::SBFrame& frame,
    lldb::SBError& error,
    PCSTR exp)
{
    ULONG64 result = 0;

    lldb::SBValue value = frame.EvaluateExpression(exp, lldb::eNoDynamicValues);
    if (value.IsValid())
    {
        result = value.GetValueAsUnsigned(error);
    }

    return result;
}

//
// lldb doesn't have a way or API to unwind an arbitary context (IP, SP)
// and return the next frame so we have to stick with the native frames
// lldb has found and find the closest frame to the incoming context SP.
//
HRESULT 
LLDBServices::VirtualUnwind(
    DWORD threadID,
    ULONG32 contextSize,
    PBYTE context)
{
    lldb::SBProcess process;
    lldb::SBThread thread;

    if (context == NULL || contextSize < sizeof(DT_CONTEXT))
    {
        return E_INVALIDARG;
    }

    process = GetCurrentProcess();
    if (!process.IsValid())
    {
        return E_FAIL;
    }

    thread = process.GetThreadByID(threadID);
    if (!thread.IsValid())
    {
        Output(DEBUG_OUTPUT_ERROR, "VirtualUnwind %08x GetThreadById FAILED\n", threadID);
        return E_FAIL;
    }

    DT_CONTEXT *dtcontext = (DT_CONTEXT*)context;
    lldb::SBFrame frameFound;

#ifdef DBG_TARGET_AMD64
    DWORD64 spToFind = dtcontext->Rsp;
#elif DBG_TARGET_X86
    DWORD spToFind = dtcontext->Esp;
#elif DBG_TARGET_ARM
    DWORD spToFind = dtcontext->Sp;
#elif DBG_TARGET_ARM64
    DWORD64 spToFind = dtcontext->Sp;
#else
#error "spToFind undefined for this platform"
#endif
    
    int numFrames = thread.GetNumFrames();
    for (int i = 0; i < numFrames; i++)
    {
        lldb::SBFrame frame = thread.GetFrameAtIndex(i);
        if (!frame.IsValid())
        {
            break;
        }
        lldb::addr_t sp = frame.GetSP();

        if ((i + 1) < numFrames)
        {
            lldb::SBFrame frameNext = thread.GetFrameAtIndex(i + 1);
            if (frameNext.IsValid())
            {
                lldb::addr_t spNext = frameNext.GetSP();

                // An exact match of the current frame's SP would be nice
                // but sometimes the incoming context is between lldb frames
                if (spToFind >= sp && spToFind < spNext)
                {
                    frameFound = frameNext;
                    break;
                }
            }
        }
    }

    if (!frameFound.IsValid())
    {
        Output(DEBUG_OUTPUT_ERROR, "VirtualUnwind %08x spToFind %016lx\n", threadID, spToFind);
        return E_FAIL;
    }

    GetContextFromFrame(frameFound, dtcontext);

    return S_OK;
}

bool 
ExceptionBreakpointCallback(
    void *baton, 
    lldb::SBProcess &process,
    lldb::SBThread &thread, 
    lldb::SBBreakpointLocation &location)
{
    lldb::SBProcess* savedProcess = g_services->SetCurrentProcess(&process);
    lldb::SBThread* savedThread = g_services->SetCurrentThread(&thread);
    g_services->FlushCheck();

    bool result = ((PFN_EXCEPTION_CALLBACK)baton)(g_services) == S_OK;

    g_services->SetCurrentProcess(savedProcess);
    g_services->SetCurrentThread(savedThread);

    return result;
}

lldb::SBBreakpoint g_exceptionbp;

HRESULT 
LLDBServices::SetExceptionCallback(
    PFN_EXCEPTION_CALLBACK callback)
{
    if (!g_exceptionbp.IsValid())
    {
        lldb::SBTarget target = m_debugger.GetSelectedTarget();
        if (!target.IsValid())
        {
            return E_FAIL;
        }
        lldb::SBBreakpoint exceptionbp = target.BreakpointCreateForException(lldb::LanguageType::eLanguageTypeC_plus_plus, false, true);
        if (!exceptionbp.IsValid())
        {
            return E_FAIL;
        }
#ifdef FLAGS_ANONYMOUS_ENUM
        exceptionbp.AddName("DoNotDeleteOrDisable");
#endif
        exceptionbp.SetCallback(ExceptionBreakpointCallback, (void *)callback);
        g_exceptionbp = exceptionbp;
    }
    return S_OK;
}

HRESULT 
LLDBServices::ClearExceptionCallback()
{
    if (g_exceptionbp.IsValid())
    {
        lldb::SBTarget target = m_debugger.GetSelectedTarget();
        if (!target.IsValid())
        {
            return E_FAIL;
        }
        target.BreakpointDelete(g_exceptionbp.GetID());
        g_exceptionbp = lldb::SBBreakpoint();
    }
    return S_OK;
}

//----------------------------------------------------------------------------
// IDebugControl2
//----------------------------------------------------------------------------

// Checks for a user interrupt, such a Ctrl-C
// or stop button.
// This method is reentrant.
HRESULT 
LLDBServices::GetInterrupt()
{
    return E_FAIL;
}

// Sends output through clients
// output callbacks if the mask is allowed
// by the current output control mask and
// according to the output distribution
// settings.
HRESULT 
LLDBServices::Output(
    ULONG mask,
    PCSTR format,
    ...)
{
    va_list args;
    va_start (args, format);
    HRESULT result = InternalOutputVaList(mask, format, args);
    va_end (args);
    return result;
}

HRESULT 
LLDBServices::OutputVaList(
    ULONG mask,
    PCSTR format,
    va_list args)
{
    // Just output the string; ignore args. It is always formatted by SOS.
    OutputString(mask, format);
    return S_OK;
}

// The following methods allow direct control
// over the distribution of the given output
// for situations where something other than
// the default is desired.  These methods require
// extra work in the engine so they should
// only be used when necessary.
HRESULT 
LLDBServices::ControlledOutput(
    ULONG outputControl,
    ULONG mask,
    PCSTR format,
    ...)
{
    va_list args;
    va_start (args, format);
    HRESULT result = ControlledOutputVaList(outputControl, mask, format, args);
    va_end (args);
    return result;
}

HRESULT 
LLDBServices::ControlledOutputVaList(
    ULONG outputControl,
    ULONG mask,
    PCSTR format,
    va_list args)
{
    return OutputVaList(mask, format, args);
}

// Returns information about the debuggee such
// as user vs. kernel, dump vs. live, etc.
HRESULT 
LLDBServices::GetDebuggeeType(
    PULONG debugClass,
    PULONG qualifier)
{
    *debugClass = DEBUG_CLASS_USER_WINDOWS; 
    *qualifier = 0;

    lldb::SBProcess process = GetCurrentProcess();
    if (process.IsValid())
    {
        const char* pluginName = process.GetPluginName();
        if ((strcmp(pluginName, "elf-core") == 0) || (strcmp(pluginName, "mach-o-core") == 0))
        {
            *qualifier = DEBUG_DUMP_FULL;
        }
    }

    return S_OK;
}

// Returns the page size for the currently executing
// processor context.  The page size may vary between
// processor types.
HRESULT 
LLDBServices::GetPageSize(
    PULONG size)
{
    *size = 4096;
    return S_OK;
}

HRESULT 
LLDBServices::GetExecutingProcessorType(
    PULONG type)
{
#ifdef DBG_TARGET_AMD64
    *type = IMAGE_FILE_MACHINE_AMD64;
#elif DBG_TARGET_ARM
    *type = IMAGE_FILE_MACHINE_ARMNT;
#elif DBG_TARGET_ARM64
    *type = IMAGE_FILE_MACHINE_ARM64;
#elif DBG_TARGET_X86
    *type = IMAGE_FILE_MACHINE_I386;
#else
#error "Unsupported target"
#endif
    return S_OK;
}

HRESULT 
LLDBServices::Execute(
    ULONG outputControl,
    PCSTR command,
    ULONG flags)
{
    lldb::SBCommandReturnObject result;
    lldb::ReturnStatus status = m_interpreter.HandleCommand(command, result);
    return status <= lldb::eReturnStatusSuccessContinuingResult ? S_OK : E_FAIL;
}

// PAL raise exception function and exception record pointer variable name
// See coreclr\src\pal\src\exception\seh-unwind.cpp for the details. This
// function depends on RtlpRaisException not being inlined or optimized.
#define FUNCTION_NAME "RtlpRaiseException"
#define VARIABLE_NAME "ExceptionRecord"

HRESULT 
LLDBServices::GetLastEventInformation(
    PULONG type,
    PULONG processId,
    PULONG threadId,
    PVOID extraInformation,
    ULONG extraInformationSize,
    PULONG extraInformationUsed,
    PSTR description,
    ULONG descriptionSize,
    PULONG descriptionUsed)
{
    if (extraInformationSize < sizeof(DEBUG_LAST_EVENT_INFO_EXCEPTION) || 
        type == NULL || processId == NULL || threadId == NULL || extraInformationUsed == NULL) 
    {
        return E_INVALIDARG;
    }

    *type = DEBUG_EVENT_EXCEPTION;
    *processId = 0;
    *threadId = 0;
    *extraInformationUsed = sizeof(DEBUG_LAST_EVENT_INFO_EXCEPTION);

    DEBUG_LAST_EVENT_INFO_EXCEPTION *pdle = (DEBUG_LAST_EVENT_INFO_EXCEPTION *)extraInformation;
    pdle->FirstChance = 1; 

    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        return E_FAIL;
    }
    lldb::SBThread thread = GetCurrentThread();
    if (!thread.IsValid())
    {
        return E_FAIL;
    }
    InitializeThreadInfo(process);

    *processId = GetProcessId(process);
    *threadId = GetThreadId(thread);

    // Enumerate each stack frame at the special "throw"
    // breakpoint and find the raise exception function 
    // with the exception record parameter.
    int numFrames = thread.GetNumFrames();
    for (int i = 0; i < numFrames; i++)
    {
        lldb::SBFrame frame = thread.GetFrameAtIndex(i);
        if (!frame.IsValid())
        {
            break;
        }

        const char *functionName = frame.GetFunctionName();
        if (functionName == NULL || strncmp(functionName, FUNCTION_NAME, sizeof(FUNCTION_NAME) - 1) != 0)
        {
            continue;
        }

        lldb::SBValue exValue = frame.FindVariable(VARIABLE_NAME);
        if (!exValue.IsValid())
        {
            break;
        }

        lldb::SBError error;
        ULONG64 pExceptionRecord = exValue.GetValueAsUnsigned(error);
        if (error.Fail())
        {
            break;
        }

        process.ReadMemory(pExceptionRecord, &pdle->ExceptionRecord, sizeof(pdle->ExceptionRecord), error);
        if (error.Fail())
        {
            break;
        }

        return S_OK;
    }

    return E_FAIL;
}

HRESULT 
LLDBServices::Disassemble(
    ULONG64 offset,
    ULONG flags,
    PSTR buffer,
    ULONG bufferSize,
    PULONG disassemblySize,
    PULONG64 endOffset)
{
    lldb::SBInstruction instruction;
    lldb::SBInstructionList list;
    lldb::SBTarget target;
    lldb::SBAddress address;
    lldb::SBError error;
    lldb::SBData data;
    std::string str;
    HRESULT hr = S_OK;
    ULONG size = 0;
    uint8_t byte;
    int cch;

    // lldb doesn't expect sign-extended address
    offset = CONVERT_FROM_SIGN_EXTENDED(offset);

    if (buffer == NULL)
    {
        hr = E_INVALIDARG;
        goto exit;
    }
    *buffer = 0;

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        hr = E_INVALIDARG;
        goto exit;
    }
    address = target.ResolveLoadAddress(offset);
    if (!address.IsValid())
    {
        hr = E_INVALIDARG;
        goto exit;
    }
    list = target.ReadInstructions(address, 1, "intel");
    if (!list.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }
    instruction = list.GetInstructionAtIndex(0);
    if (!instruction.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }
    cch = snprintf(buffer, bufferSize, "%016llx ", (unsigned long long)offset);
    buffer += cch;
    bufferSize -= cch;

    size = instruction.GetByteSize();
    data = instruction.GetData(target);
    for (ULONG i = 0; i < size && bufferSize > 0; i++)
    {
        byte = data.GetUnsignedInt8(error, i);
        if (error.Fail())
        {
            hr = E_FAIL;
            goto exit;
        }
        cch = snprintf(buffer, bufferSize, "%02x", byte);
        buffer += cch;
        bufferSize -= cch;
    }
    // Pad the data bytes to 16 chars
    cch = size * 2;
    while (bufferSize > 0)
    {
        *buffer++ = ' ';
        bufferSize--;
        if (++cch >= 21)
            break;
    } 

    cch = snprintf(buffer, bufferSize, "%s", instruction.GetMnemonic(target));
    buffer += cch;
    bufferSize -= cch;

    // Pad the mnemonic to 8 chars
    while (bufferSize > 0)
    {
        *buffer++ = ' ';
        bufferSize--;
        if (++cch >= 8)
            break;
    } 
    snprintf(buffer, bufferSize, "%s\n", instruction.GetOperands(target));

exit:
    if (disassemblySize != NULL)
    {
        *disassemblySize = size;
    }
    if (endOffset != NULL)
    {
        *endOffset = offset + size;
    }
    return hr;
}

//----------------------------------------------------------------------------
// IDebugControl4
//----------------------------------------------------------------------------

HRESULT
LLDBServices::GetContextStackTrace(
    PVOID startContext,
    ULONG startContextSize,
    PDEBUG_STACK_FRAME frames,
    ULONG framesSize,
    PVOID frameContexts,
    ULONG frameContextsSize,
    ULONG frameContextsEntrySize,
    PULONG framesFilled)
{
    DT_CONTEXT *currentContext = (DT_CONTEXT*)frameContexts;
    PDEBUG_STACK_FRAME currentFrame = frames;
    lldb::SBThread thread;
    lldb::SBFrame frame;
    ULONG cFrames = 0;
    HRESULT hr = S_OK;

    // Doesn't support a starting context
    if (startContext != NULL || frames == NULL || frameContexts == NULL || frameContextsEntrySize != sizeof(DT_CONTEXT))
    {
        hr = E_INVALIDARG;
        goto exit;
    }

    thread = GetCurrentThread();
    if (!thread.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }

    frame = thread.GetFrameAtIndex(0);
    for (uint32_t i = 0; i < thread.GetNumFrames(); i++)
    {
        if (!frame.IsValid() || (cFrames > framesSize) || ((char *)currentContext > ((char *)frameContexts + frameContextsSize)))
        {
            break;
        }
        lldb::SBFrame framePrevious;
        lldb::SBFrame frameNext;

        currentFrame->InstructionOffset = frame.GetPC();
        currentFrame->StackOffset = frame.GetSP();

        currentFrame->FuncTableEntry = 0;
        currentFrame->Params[0] = 0;
        currentFrame->Params[1] = 0;
        currentFrame->Params[2] = 0;
        currentFrame->Params[3] = 0;
        currentFrame->Virtual = i == 0 ? TRUE : FALSE;
        currentFrame->FrameNumber = frame.GetFrameID();

        frameNext = thread.GetFrameAtIndex(i + 1);
        if (frameNext.IsValid())
        {
            currentFrame->ReturnOffset = frameNext.GetPC();
        }

        if (framePrevious.IsValid())
        {
            currentFrame->FrameOffset = framePrevious.GetSP();
        }
        else
        {
            currentFrame->FrameOffset = frame.GetSP();
        }

        GetContextFromFrame(frame, currentContext);

        framePrevious = frame;
        frame = frameNext;
        currentContext++;
        currentFrame++;
        cFrames++;
    }

exit:
    if (framesFilled != NULL)
    {
        *framesFilled = cFrames;
    }
    return hr;
}
    
//----------------------------------------------------------------------------
// IDebugDataSpaces
//----------------------------------------------------------------------------

HRESULT 
LLDBServices::ReadVirtual(
    ULONG64 offset,
    PVOID buffer,
    ULONG bufferSize,
    PULONG bytesRead)
{
    lldb::SBError error;
    size_t read = 0;

    // lldb doesn't expect sign-extended address
    offset = CONVERT_FROM_SIGN_EXTENDED(offset);

    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        goto exit;
    }

    read = process.ReadMemory(offset, buffer, bufferSize, error);

    if (!error.Success())
    {
        lldb::SBTarget target = process.GetTarget();
        if (!target.IsValid())
        {
            goto exit;
        }

        int numModules = target.GetNumModules();
        bool found = false;
        for (int i = 0; !found && i < numModules; i++)
        {
            lldb::SBModule module = target.GetModuleAtIndex(i);
            int numSections = module.GetNumSections();
            for (int j = 0; j < numSections; j++)
            {
                lldb::SBSection section = module.GetSectionAtIndex(j);
                lldb::addr_t loadAddr = section.GetLoadAddress(target);
                lldb::addr_t byteSize = section.GetByteSize();
                if ((loadAddr != LLDB_INVALID_ADDRESS) && (offset >= loadAddr) && (offset < loadAddr + byteSize))
                {
                    lldb::SBData sectionData = section.GetSectionData(offset - loadAddr, bufferSize);
                    read = sectionData.ReadRawData(error, 0, buffer, bufferSize);
                    found = true;
                    break;
                }
            }
        }
    }

exit:
    if (bytesRead)
    {
        *bytesRead = read;
    }
    return error.Success() || (read != 0) ? S_OK : E_FAIL;
}

HRESULT 
LLDBServices::WriteVirtual(
    ULONG64 offset,
    PVOID buffer,
    ULONG bufferSize,
    PULONG bytesWritten)
{
    lldb::SBError error;
    size_t written = 0;

    // lldb doesn't expect sign-extended address
    offset = CONVERT_FROM_SIGN_EXTENDED(offset);

    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        goto exit;
    }

    written = process.WriteMemory(offset, buffer, bufferSize, error);

exit:
    if (bytesWritten)
    {
        *bytesWritten = written;
    }
    return error.Success() || (written != 0) ? S_OK : E_FAIL;
}

//----------------------------------------------------------------------------
// IDebugSymbols
//----------------------------------------------------------------------------

HRESULT 
LLDBServices::GetSymbolOptions(
    PULONG options)
{
    *options = SYMOPT_LOAD_LINES;
    return S_OK;
}

HRESULT 
LLDBServices::GetNameByOffset(
    ULONG64 offset,
    PSTR nameBuffer,
    ULONG nameBufferSize,
    PULONG nameSize,
    PULONG64 displacement)
{
    return GetNameByOffset(DEBUG_ANY_ID, offset, nameBuffer, nameBufferSize, nameSize, displacement);
}

HRESULT 
LLDBServices::GetNameByOffset(
    ULONG moduleIndex,
    ULONG64 offset,
    PSTR nameBuffer,
    ULONG nameBufferSize,
    PULONG nameSize,
    PULONG64 displacement)
{
    ULONG64 disp = DEBUG_INVALID_OFFSET;
    HRESULT hr = S_OK;

    lldb::SBTarget target;
    lldb::SBAddress address;
    lldb::SBModule module;
    lldb::SBFileSpec file;
    lldb::SBSymbol symbol;
    std::string str;

    // lldb doesn't expect sign-extended address
    offset = CONVERT_FROM_SIGN_EXTENDED(offset);

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }

    address = target.ResolveLoadAddress(offset);
    if (!address.IsValid())
    {
        hr = E_INVALIDARG;
        goto exit;
    }

    // If module index is invalid, add module name to symbol
    if (moduleIndex == DEBUG_ANY_ID)
    {
        module = address.GetModule();
        if (!module.IsValid())
        {
            hr = E_FAIL;
            goto exit;
        }

        file = module.GetFileSpec();
        if (file.IsValid())
        {
            str.append(file.GetFilename());
        }
    }
    else 
    {
        module = target.GetModuleAtIndex(moduleIndex);
        if (!module.IsValid())
        {
            hr = E_INVALIDARG;
            goto exit;
        }

        if (module != address.GetModule())
        {
            hr = E_INVALIDARG;
            goto exit;
        }
    }

    symbol = address.GetSymbol();
    if (symbol.IsValid())
    {
        lldb::SBAddress startAddress = symbol.GetStartAddress();
        disp = address.GetOffset() - startAddress.GetOffset();

        const char *name = symbol.GetName();
        if (name)
        {
            if (file.IsValid())
            {
                str.append("!");
            }
            str.append(name);
        }
    }

    str.append(1, '\0');

exit:
    if (nameSize)
    {
        *nameSize = str.length();
    }
    if (nameBuffer)
    {
        str.copy(nameBuffer, nameBufferSize);
    }
    if (displacement)
    {
        *displacement = disp;
    }
    return hr;
}

HRESULT 
LLDBServices::GetNumberModules(
    PULONG loaded,
    PULONG unloaded)
{
    ULONG numModules = 0;
    HRESULT hr = S_OK;

    lldb::SBTarget target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }

    numModules = target.GetNumModules();

exit:
    if (loaded)
    {
        *loaded = numModules;
    }
    if (unloaded)
    {
        *unloaded = 0;
    }
    return hr;
}

HRESULT LLDBServices::GetModuleByIndex(
    ULONG index,
    PULONG64 base)
{
    lldb::SBTarget target;
    lldb::SBModule module;
    
    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        return E_INVALIDARG;
    }

    module = target.GetModuleAtIndex(index);
    if (!module.IsValid())
    {
        return E_INVALIDARG;
    }

    if (base)
    {
        ULONG64 moduleBase = GetModuleBase(target, module);
        if (moduleBase == UINT64_MAX)
        {
            return E_INVALIDARG;
        }
        *base = moduleBase;
    }
    return S_OK;
}

HRESULT 
LLDBServices::GetModuleByModuleName(
    PCSTR name,
    ULONG startIndex,
    PULONG index,
    PULONG64 base)
{
    lldb::SBTarget target;
    lldb::SBModule module;
    lldb::SBFileSpec fileSpec;
    fileSpec.SetFilename(name);

    if (startIndex != 0)
    {
        return E_INVALIDARG;
    }

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        return E_INVALIDARG;
    }

    module = target.FindModule(fileSpec);
    if (!module.IsValid())
    {
        return E_INVALIDARG;
    }

    if (base)
    {
        ULONG64 moduleBase = GetModuleBase(target, module);
        if (moduleBase == UINT64_MAX)
        {
            return E_INVALIDARG;
        }
        *base = moduleBase;
    }

    if (index)
    {
        *index = 0;
        int numModules = target.GetNumModules();
        for (int mi = 0; mi < numModules; mi++)
        {
            lldb::SBModule mod = target.GetModuleAtIndex(mi);
            if (module == mod)
            {
                *index = mi;
                break;
            }
        }
    }

    return S_OK;
}

HRESULT 
LLDBServices::GetModuleByOffset(
    ULONG64 offset,
    ULONG startIndex,
    PULONG index,
    PULONG64 base)
{
    lldb::SBTarget target;
    int numModules;

    // lldb doesn't expect sign-extended address
    offset = CONVERT_FROM_SIGN_EXTENDED(offset);

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        return E_INVALIDARG;
    }

    numModules = target.GetNumModules();
    for (int mi = startIndex; mi < numModules; mi++)
    {
        lldb::SBModule module = target.GetModuleAtIndex(mi);

        int numSections = module.GetNumSections();
        for (int si = 0; si < numSections; si++)
        {
            lldb::SBSection section = module.GetSectionAtIndex(si);
            if (section.IsValid())
            {
                lldb::addr_t baseAddress = section.GetLoadAddress(target);
                if (baseAddress != LLDB_INVALID_ADDRESS)
                {
                    if (offset >= baseAddress)
                    {
                        if ((offset - baseAddress) < section.GetByteSize())
                        {
                            if (index)
                            {
                                *index = mi;
                            }
                            if (base)
                            {
                                *base = baseAddress - section.GetFileOffset();
                            }
                            return S_OK;
                        }
                    }
                }
            }
        }
    }

    return E_FAIL;
}

HRESULT 
LLDBServices::GetModuleNames(
    ULONG index,
    ULONG64 base,
    PSTR imageNameBuffer,
    ULONG imageNameBufferSize,
    PULONG imageNameSize,
    PSTR moduleNameBuffer,
    ULONG moduleNameBufferSize,
    PULONG moduleNameSize,
    PSTR loadedImageNameBuffer,
    ULONG loadedImageNameBufferSize,
    PULONG loadedImageNameSize)
{
    lldb::SBTarget target;
    lldb::SBFileSpec fileSpec;

    // lldb doesn't expect sign-extended address
    base = CONVERT_FROM_SIGN_EXTENDED(base);

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        return E_INVALIDARG;
    }
    if (index != DEBUG_ANY_ID)
    {
        lldb::SBModule module = target.GetModuleAtIndex(index);
        if (module.IsValid())
        {
            fileSpec = module.GetFileSpec();
        }
    }
    else
    {
        int numModules = target.GetNumModules();
        for (int mi = 0; mi < numModules; mi++)
        {
            lldb::SBModule module = target.GetModuleAtIndex(mi);
            if (module.IsValid())
            {
                ULONG64 moduleBase = GetModuleBase(target, module);
                if (base == moduleBase)
                {
                    fileSpec = module.GetFileSpec();
                    break;
                }
            }
        }
    }
    if (!fileSpec.IsValid())
    {
        return E_INVALIDARG;
    }
    if (imageNameBuffer)
    {
        int size = fileSpec.GetPath(imageNameBuffer, imageNameBufferSize);
        if (imageNameSize)
        {
            *imageNameSize = size + 1; // include null
        }
    }
    if (moduleNameBuffer)
    {
        const char *fileName = fileSpec.GetFilename();
        if (fileName == NULL)
        {
            fileName = "";
        }
        stpncpy(moduleNameBuffer, fileName, moduleNameBufferSize);
        if (moduleNameSize)
        {
            *moduleNameSize = strlen(fileName) + 1;
        }
    }
    if (loadedImageNameBuffer)
    {
        int size = fileSpec.GetPath(loadedImageNameBuffer, loadedImageNameBufferSize);
        if (loadedImageNameSize)
        {
            *loadedImageNameSize = size + 1; // include null
        }
    }
    return S_OK;
}

HRESULT 
LLDBServices::GetLineByOffset(
    ULONG64 offset,
    PULONG fileLine,
    PSTR fileBuffer,
    ULONG fileBufferSize,
    PULONG fileSize,
    PULONG64 displacement)
{
    ULONG64 disp = DEBUG_INVALID_OFFSET;
    HRESULT hr = S_OK;
    ULONG line = 0;

    lldb::SBTarget target;
    lldb::SBAddress address;
    lldb::SBFileSpec file;
    lldb::SBLineEntry lineEntry;
    std::string str;

    // lldb doesn't expect sign-extended address
    offset = CONVERT_FROM_SIGN_EXTENDED(offset);

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }

    address = target.ResolveLoadAddress(offset);
    if (!address.IsValid())
    {
        hr = E_INVALIDARG;
        goto exit;
    }

    if (displacement)
    {
        lldb::SBSymbol symbol = address.GetSymbol();
        if (symbol.IsValid())
        {
            lldb::SBAddress startAddress = symbol.GetStartAddress();
            disp = address.GetOffset() - startAddress.GetOffset();
        }
    }

    lineEntry = address.GetLineEntry();
    if (!lineEntry.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }

    line = lineEntry.GetLine();
    file = lineEntry.GetFileSpec();
    if (file.IsValid())
    {
        str.append(file.GetDirectory());
        str.append(1, '/');
        str.append(file.GetFilename());
    }

    str.append(1, '\0');

exit:
    if (fileLine)
    {
        *fileLine = line;
    }
    if (fileSize)
    {
        *fileSize = str.length();
    }
    if (fileBuffer)
    {
        str.copy(fileBuffer, fileBufferSize);
    }
    if (displacement)
    {
        *displacement = disp;
    }
    return hr;
}
 
HRESULT 
LLDBServices::GetSourceFileLineOffsets(
    PCSTR file,
    PULONG64 buffer,
    ULONG bufferLines,
    PULONG fileLines)
{
    if (fileLines != NULL)
    {
        *fileLines = (ULONG)-1;
    }
    return E_NOTIMPL;
}

HRESULT 
LLDBServices::FindSourceFile(
    ULONG startElement,
    PCSTR file,
    ULONG flags,
    PULONG foundElement,
    PSTR buffer,
    ULONG bufferSize,
    PULONG foundSize)
{
    return E_NOTIMPL;
}

// Internal functions

ULONG64
LLDBServices::GetModuleBase(
    /* const */ lldb::SBTarget& target,
    /* const */ lldb::SBModule& module)
{
    // Find the first section with an valid base address
    int numSections = module.GetNumSections();
    for (int si = 0; si < numSections; si++)
    {
        lldb::SBSection section = module.GetSectionAtIndex(si);
        if (section.IsValid())
        {
            lldb::addr_t baseAddress = section.GetLoadAddress(target);
            if (baseAddress != LLDB_INVALID_ADDRESS)
            {
                return baseAddress - section.GetFileOffset();
            }
        }
    }

    lldb::SBAddress headerAddress = module.GetObjectFileHeaderAddress();
    lldb::addr_t moduleAddress = headerAddress.GetLoadAddress(target);
    if (moduleAddress != 0)
    {
        return moduleAddress;
    }

    return UINT64_MAX;
}

ULONG64
LLDBServices::GetModuleSize(
    ULONG64 baseAddress,
    /* const */ lldb::SBModule& module)
{
    ULONG64 size = 0;
#if defined(__APPLE__)
    mach_header_64 header;
    ULONG read;
    HRESULT hr = ReadVirtual(baseAddress, &header, sizeof(mach_header_64), &read) == S_OK;
    if (SUCCEEDED(hr))
    {
	    // Since MachO segments are not contiguous the image size is just the headers/commands
	    size = sizeof(mach_header_64) + header.sizeofcmds;
    }
#else
    // Find the first section with an valid base address
    int numSections = module.GetNumSections();
    for (int si = 0; si < numSections; si++)
    {
        lldb::SBSection section = module.GetSectionAtIndex(si);
        if (section.IsValid())
        {
            size += section.GetByteSize();
        }
    }
#endif
    // For core dumps lldb doesn't return the section sizes when it 
    // doesn't have access to the actual module file, but SOS (like 
    // the SymbolReader code) still needs a non-zero module size.
    return size != 0 ? size : LONG_MAX;
}

//----------------------------------------------------------------------------
// IDebugSystemObjects
//----------------------------------------------------------------------------

HRESULT 
LLDBServices::GetCurrentProcessSystemId(
    PULONG sysId)
{
    if (sysId == NULL)  
    {
        return E_INVALIDARG;
    }

    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        *sysId = 0;
        return E_FAIL;
    }

    InitializeThreadInfo(process);

    *sysId = GetProcessId(process);
    return S_OK;
}

HRESULT 
LLDBServices::GetCurrentThreadId(
    PULONG id)
{
    if (id == NULL)  
    {
        return E_INVALIDARG;
    }

    lldb::SBThread thread = GetCurrentThread();
    if (!thread.IsValid())
    {
        *id = 0;
        return E_FAIL;
    }

    *id = thread.GetIndexID();
    return S_OK;
}

HRESULT 
LLDBServices::SetCurrentThreadId(
    ULONG id)
{
    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        return E_FAIL;
    }

    if (!process.SetSelectedThreadByIndexID(id))
    {
        return E_FAIL;
    }

    return S_OK;
}

HRESULT 
LLDBServices::GetCurrentThreadSystemId(
    PULONG sysId)
{
    if (sysId == NULL)  
    {
        return E_INVALIDARG;
    }

    lldb::SBThread thread = GetCurrentThread();
    if (!thread.IsValid())
    {
        *sysId = 0;
        return E_FAIL;
    }

    *sysId = GetThreadId(thread);
    return S_OK;
}

HRESULT 
LLDBServices::GetThreadIdBySystemId(
    ULONG sysId,
    PULONG threadId)
{

    if (threadId == NULL)  
    {
        return E_INVALIDARG;
    }

    lldb::SBThread thread = GetThreadBySystemId(sysId);
    if (!thread.IsValid())
    {
        *threadId = 0;
        return E_FAIL;
    }

    *threadId = thread.GetIndexID();
    return S_OK;
}

HRESULT 
LLDBServices::GetThreadContextBySystemId(
    /* in */ ULONG32 sysId,
    /* in */ ULONG32 contextFlags,
    /* in */ ULONG32 contextSize,
    /* out */ PBYTE context)
{
    lldb::SBProcess process;
    lldb::SBThread thread;
    lldb::SBFrame frame;
    DT_CONTEXT *dtcontext;
    HRESULT hr = E_FAIL;

    if (context == NULL || contextSize < sizeof(DT_CONTEXT))
    {
        goto exit;
    }
    memset(context, 0, contextSize);

    thread = GetThreadBySystemId(sysId);
    if (!thread.IsValid())
    {
        goto exit;
    }

    frame = thread.GetFrameAtIndex(0);
    if (!frame.IsValid())
    {
        goto exit;
    }

    dtcontext = (DT_CONTEXT*)context;
    dtcontext->ContextFlags = contextFlags;

    GetContextFromFrame(frame, dtcontext);
    hr = S_OK;

exit:
    return hr;
}

// Internal function
void
LLDBServices::GetContextFromFrame(
    /* const */ lldb::SBFrame& frame,
    DT_CONTEXT *dtcontext)
{
#ifdef DBG_TARGET_AMD64
    dtcontext->Rip = frame.GetPC();
    dtcontext->Rsp = frame.GetSP();
    dtcontext->Rbp = frame.GetFP();
    dtcontext->EFlags = GetRegister(frame, "rflags");

    dtcontext->Rax = GetRegister(frame, "rax");
    dtcontext->Rbx = GetRegister(frame, "rbx");
    dtcontext->Rcx = GetRegister(frame, "rcx");
    dtcontext->Rdx = GetRegister(frame, "rdx");
    dtcontext->Rsi = GetRegister(frame, "rsi");
    dtcontext->Rdi = GetRegister(frame, "rdi");
    dtcontext->R8 = GetRegister(frame, "r8");
    dtcontext->R9 = GetRegister(frame, "r9");
    dtcontext->R10 = GetRegister(frame, "r10");
    dtcontext->R11 = GetRegister(frame, "r11");
    dtcontext->R12 = GetRegister(frame, "r12");
    dtcontext->R13 = GetRegister(frame, "r13");
    dtcontext->R14 = GetRegister(frame, "r14");
    dtcontext->R15 = GetRegister(frame, "r15");

    dtcontext->SegCs = GetRegister(frame, "cs");
    dtcontext->SegSs = GetRegister(frame, "ss");
    dtcontext->SegDs = GetRegister(frame, "ds");
    dtcontext->SegEs = GetRegister(frame, "es");
    dtcontext->SegFs = GetRegister(frame, "fs");
    dtcontext->SegGs = GetRegister(frame, "gs");
#elif DBG_TARGET_ARM
    dtcontext->Pc = frame.GetPC();
    dtcontext->Sp = frame.GetSP();
    dtcontext->Lr = GetRegister(frame, "lr");
    dtcontext->Cpsr = GetRegister(frame, "cpsr");

    dtcontext->R0 = GetRegister(frame, "r0");
    dtcontext->R1 = GetRegister(frame, "r1");
    dtcontext->R2 = GetRegister(frame, "r2");
    dtcontext->R3 = GetRegister(frame, "r3");
    dtcontext->R4 = GetRegister(frame, "r4");
    dtcontext->R5 = GetRegister(frame, "r5");
    dtcontext->R6 = GetRegister(frame, "r6");
    dtcontext->R7 = GetRegister(frame, "r7");
    dtcontext->R8 = GetRegister(frame, "r8");
    dtcontext->R9 = GetRegister(frame, "r9");
    dtcontext->R10 = GetRegister(frame, "r10");
    dtcontext->R11 = GetRegister(frame, "r11");
    dtcontext->R12 = GetRegister(frame, "r12");
#elif DBG_TARGET_ARM64
    dtcontext->Pc = frame.GetPC();
    dtcontext->Sp = frame.GetSP();
    dtcontext->Lr = GetRegister(frame, "x30");
    dtcontext->Fp = GetRegister(frame, "x29");
    dtcontext->Cpsr = GetRegister(frame, "cpsr");

    dtcontext->X0 = GetRegister(frame, "x0");
    dtcontext->X1 = GetRegister(frame, "x1");
    dtcontext->X2 = GetRegister(frame, "x2");
    dtcontext->X3 = GetRegister(frame, "x3");
    dtcontext->X4 = GetRegister(frame, "x4");
    dtcontext->X5 = GetRegister(frame, "x5");
    dtcontext->X6 = GetRegister(frame, "x6");
    dtcontext->X7 = GetRegister(frame, "x7");
    dtcontext->X8 = GetRegister(frame, "x8");
    dtcontext->X9 = GetRegister(frame, "x9");
    dtcontext->X10 = GetRegister(frame, "x10");
    dtcontext->X11 = GetRegister(frame, "x11");
    dtcontext->X12 = GetRegister(frame, "x12");
    dtcontext->X13 = GetRegister(frame, "x13");
    dtcontext->X14 = GetRegister(frame, "x14");
    dtcontext->X15 = GetRegister(frame, "x15");
    dtcontext->X16 = GetRegister(frame, "x16");
    dtcontext->X17 = GetRegister(frame, "x17");
    dtcontext->X18 = GetRegister(frame, "x18");
    dtcontext->X19 = GetRegister(frame, "x19");
    dtcontext->X20 = GetRegister(frame, "x20");
    dtcontext->X21 = GetRegister(frame, "x21");
    dtcontext->X22 = GetRegister(frame, "x22");
    dtcontext->X23 = GetRegister(frame, "x23");
    dtcontext->X24 = GetRegister(frame, "x24");
    dtcontext->X25 = GetRegister(frame, "x25");
    dtcontext->X26 = GetRegister(frame, "x26");
    dtcontext->X27 = GetRegister(frame, "x27");
    dtcontext->X28 = GetRegister(frame, "x28");
#elif DBG_TARGET_X86
    dtcontext->Eip = frame.GetPC();
    dtcontext->Esp = frame.GetSP();
    dtcontext->Ebp = frame.GetFP();
    dtcontext->EFlags = GetRegister(frame, "eflags");

    dtcontext->Edi = GetRegister(frame, "edi");
    dtcontext->Esi = GetRegister(frame, "esi");
    dtcontext->Ebx = GetRegister(frame, "ebx");
    dtcontext->Edx = GetRegister(frame, "edx");
    dtcontext->Ecx = GetRegister(frame, "ecx");
    dtcontext->Eax = GetRegister(frame, "eax");

    dtcontext->SegCs = GetRegister(frame, "cs");
    dtcontext->SegSs = GetRegister(frame, "ss");
    dtcontext->SegDs = GetRegister(frame, "ds");
    dtcontext->SegEs = GetRegister(frame, "es");
    dtcontext->SegFs = GetRegister(frame, "fs");
    dtcontext->SegGs = GetRegister(frame, "gs");
#endif
}

// Internal function
DWORD_PTR 
LLDBServices::GetRegister(
    /* const */ lldb::SBFrame& frame,
    const char *name)
{
    lldb::SBValue regValue = frame.FindRegister(name);

    lldb::SBError error;
    DWORD_PTR result = regValue.GetValueAsUnsigned(error);

    return result;
}

//----------------------------------------------------------------------------
// IDebugRegisters
//----------------------------------------------------------------------------

HRESULT
LLDBServices::GetValueByName(
    PCSTR name,
    PDWORD_PTR debugValue)
{
    lldb::SBFrame frame = GetCurrentFrame();
    if (!frame.IsValid())
    {
        *debugValue = 0;
        return E_FAIL;
    }

    lldb::SBValue value = frame.FindRegister(name);
    if (!value.IsValid())
    {
        *debugValue = 0;
        return E_FAIL;
    }

    *debugValue = value.GetValueAsUnsigned();
    return S_OK;
}

HRESULT 
LLDBServices::GetInstructionOffset(
    PULONG64 offset)
{
    lldb::SBFrame frame = GetCurrentFrame();
    if (!frame.IsValid())
    {
        *offset = 0;
        return E_FAIL;
    }

    *offset = frame.GetPC();
    return S_OK;
}

HRESULT 
LLDBServices::GetStackOffset(
    PULONG64 offset)
{
    lldb::SBFrame frame = GetCurrentFrame();
    if (!frame.IsValid())
    {
        *offset = 0;
        return E_FAIL;
    }

    *offset = frame.GetSP();
    return S_OK;
}

HRESULT 
LLDBServices::GetFrameOffset(
    PULONG64 offset)
{
    lldb::SBFrame frame = GetCurrentFrame();
    if (!frame.IsValid())
    {
        *offset = 0;
        return E_FAIL;
    }

    *offset = frame.GetFP();
    return S_OK;
}

//----------------------------------------------------------------------------
// ILLDBServices2
//----------------------------------------------------------------------------

void
LLDBServices::LoadNativeSymbols(
    lldb::SBTarget target,
    lldb::SBModule module,
    PFN_MODULE_LOAD_CALLBACK callback)
{
    if (module.IsValid())
    {
        const char* directory = nullptr;
        const char* filename = nullptr;

        lldb::SBFileSpec symbolFileSpec = module.GetSymbolFileSpec();
        if (symbolFileSpec.IsValid())
        {
            directory = symbolFileSpec.GetDirectory();
            filename = symbolFileSpec.GetFilename();
        }
        else {
            lldb::SBFileSpec fileSpec = module.GetFileSpec();
            if (fileSpec.IsValid())
            {
                directory = fileSpec.GetDirectory();
                filename = fileSpec.GetFilename();
            }
        }

        if (directory != nullptr && filename != nullptr)
        {
            ULONG64 moduleAddress = GetModuleBase(target, module);
            if (moduleAddress != UINT64_MAX)
            {
                std::string path(directory);
                path.append("/");
                path.append(filename);

                int moduleSize = GetModuleSize(moduleAddress, module);

                callback(&module, path.c_str(), moduleAddress, moduleSize);
            }
        }
    }
}

HRESULT 
LLDBServices::LoadNativeSymbols(
    bool runtimeOnly,
    PFN_MODULE_LOAD_CALLBACK callback)
{
    if (runtimeOnly)
    {
        lldb::SBTarget target = m_debugger.GetSelectedTarget();
        if (target.IsValid())
        {
            const char *coreclrModule = MAKEDLLNAME_A("coreclr");
            lldb::SBFileSpec fileSpec;
            fileSpec.SetFilename(coreclrModule);

            lldb::SBModule module = target.FindModule(fileSpec);
            LoadNativeSymbols(target, module, callback);
        }
    }
    else 
    {
        uint32_t numTargets = m_debugger.GetNumTargets();
        for (int ti = 0; ti < numTargets; ti++)
        {
            lldb::SBTarget target = m_debugger.GetTargetAtIndex(ti);
            if (target.IsValid())
            {
                uint32_t numModules = target.GetNumModules();
                for (int mi = 0; mi < numModules; mi++)
                {
                    lldb::SBModule module = target.GetModuleAtIndex(mi);
                    LoadNativeSymbols(target, module, callback);
                }
            }
        }
    }
    return S_OK;
}

HRESULT 
LLDBServices::AddModuleSymbol(
    void* param,
    const char* symbolFileName)
{
    std::string command;
    command.append("target symbols add ");
    command.append(symbolFileName);

    return Execute(DEBUG_EXECUTE_NOT_LOGGED, command.c_str(), 0);
}

HRESULT LLDBServices::GetModuleInfo(
    ULONG index,
    PULONG64 pBase,
    PULONG64 pSize,
    PULONG pTimestamp,
    PULONG pChecksum)
{
    lldb::SBTarget target; 
    lldb::SBModule module;

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        return E_INVALIDARG;
    }
    module = target.GetModuleAtIndex(index);
    if (!module.IsValid())
    {
        return E_INVALIDARG;
    }
    ULONG64 moduleBase = GetModuleBase(target, module);
    if (pBase)
    {
        if (moduleBase == UINT64_MAX)
        {
            return E_INVALIDARG;
        }
        *pBase = moduleBase;
    }
    if (pSize)
    {
        *pSize = GetModuleSize(moduleBase, module);
    }
    if (pTimestamp)
    {
        *pTimestamp = InvalidTimeStamp;
    }
    if (pChecksum)
    {
        *pChecksum = InvalidChecksum;
    }
    return S_OK;
}

#define VersionBufferSize 1024

HRESULT 
LLDBServices::GetModuleVersionInformation(
    ULONG index,
    ULONG64 base,
    PCSTR item,
    PVOID buffer,
    ULONG bufferSize,
    PULONG versionInfoSize)
{
    // Only support a narrow set of argument values
    if (index == DEBUG_ANY_ID || buffer == nullptr || versionInfoSize != nullptr)
    {
        return E_INVALIDARG;
    }
    lldb::SBTarget target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        return E_INVALIDARG;
    }
    lldb::SBModule module = target.GetModuleAtIndex(index);
    if (!module.IsValid())
    {
        return E_INVALIDARG;
    }
    const char* versionString = nullptr;
    lldb::SBValue value;
    lldb::SBData data;

    value = module.FindFirstGlobalVariable(target, "sccsid");
    if (value.IsValid())
    {
        data = value.GetData();
        if (data.IsValid())
        {
            lldb::SBError error;
            versionString = data.GetString(error, 0);
            if (error.Fail())
            {
                versionString = nullptr;
            }
        }
    }

    ArrayHolder<char> versionBuffer = nullptr;
    if (versionString == nullptr)
    {
        versionBuffer = new char[VersionBufferSize];

        int numSections = module.GetNumSections();
        for (int si = 0; si < numSections; si++)
        {
            lldb::SBSection section = module.GetSectionAtIndex(si);
            if (GetVersionStringFromSection(target, section, versionBuffer.GetPtr()))
            {
                versionString = versionBuffer;
                break;
            }
        }
    }

    if (versionString == nullptr)
    {
        return E_FAIL;
    }

    if (strcmp(item, "\\") == 0)
    {
        if (bufferSize < sizeof(VS_FIXEDFILEINFO))
        {
            return E_INVALIDARG;
        }
        DWORD major, minor, build, revision;
        if (sscanf(versionString, "@(#)Version %u.%u.%u.%u", &major, &minor, &build, &revision) != 4)
        {
            return E_FAIL;
        }
        memset(buffer, 0, sizeof(VS_FIXEDFILEINFO));
        ((VS_FIXEDFILEINFO*)buffer)->dwFileVersionMS = MAKELONG(minor, major);
        ((VS_FIXEDFILEINFO*)buffer)->dwFileVersionLS = MAKELONG(revision, build);
    }
    else if (strcmp(item, "\\StringFileInfo\\040904B0\\FileVersion") == 0)
    {
        if (bufferSize < (strlen(versionString) - sizeof("@(#)Version")))
        {
            return E_INVALIDARG;
        }
        stpncpy((char*)buffer, versionString + sizeof("@(#)Version"), bufferSize);
    }
    else
    {
        return E_INVALIDARG;
    }
    return S_OK;
}

lldb::SBBreakpoint g_runtimeLoadedBp;

bool 
RuntimeLoadedBreakpointCallback(
    void *baton, 
    lldb::SBProcess &process,
    lldb::SBThread &thread, 
    lldb::SBBreakpointLocation &location)
{
    lldb::SBProcess* savedProcess = g_services->SetCurrentProcess(&process);
    lldb::SBThread* savedThread = g_services->SetCurrentThread(&thread);
    g_services->FlushCheck();

    bool result = ((PFN_RUNTIME_LOADED_CALLBACK)baton)(g_services) == S_OK;

    g_services->SetCurrentProcess(savedProcess);
    g_services->SetCurrentThread(savedThread);

    // Clear the breakpoint
    if (g_runtimeLoadedBp.IsValid())
    {
        process.GetTarget().BreakpointDelete(g_runtimeLoadedBp.GetID());
        g_runtimeLoadedBp = lldb::SBBreakpoint();
    }

    // Continue the process
    if (result)
    {
        lldb::SBError error = process.Continue();
        result = error.Success();
    }
    return result;
}

HRESULT 
LLDBServices::SetRuntimeLoadedCallback(
    PFN_RUNTIME_LOADED_CALLBACK callback)
{
    if (!g_runtimeLoadedBp.IsValid())
    {
        lldb::SBTarget target = m_debugger.GetSelectedTarget();
        if (!target.IsValid())
        {
            return E_FAIL;
        }
        // By the time the host calls coreclr_execute_assembly, the coreclr DAC table should be initialized so DAC can be loaded.
        lldb::SBBreakpoint runtimeLoadedBp = target.BreakpointCreateByName("coreclr_execute_assembly", MAKEDLLNAME_A("coreclr"));
        if (!runtimeLoadedBp.IsValid())
        {
            return E_FAIL;
        }
#ifdef FLAGS_ANONYMOUS_ENUM
        runtimeLoadedBp.AddName("DoNotDeleteOrDisable");
#endif
        runtimeLoadedBp.SetCallback(RuntimeLoadedBreakpointCallback, (void *)callback);
        g_runtimeLoadedBp = runtimeLoadedBp;
    }
    return S_OK;
}

//----------------------------------------------------------------------------
// IDebuggerServices
//----------------------------------------------------------------------------

HRESULT LLDBServices::GetOperatingSystem(
    IDebuggerServices::OperatingSystem* operatingSystem)
{
    if (operatingSystem == nullptr)
    {
        return E_INVALIDARG;
    }
#if defined(__APPLE__)
    *operatingSystem = IDebuggerServices::OperatingSystem::OSX;
#elif defined(__linux__)
    *operatingSystem = IDebuggerServices::OperatingSystem::Linux;
#else
    *operatingSystem = IDebuggerServices::OperatingSystem::Unknown;
#endif
    return S_OK;
}

class ExtensionCommand : public lldb::SBCommandPluginInterface
{
    const char *m_commandName;

public:
    ExtensionCommand(const char* commandName)
    {
        m_commandName = strdup(commandName);
    }

    ~ExtensionCommand()
    {
        g_services->Output(DEBUG_OUTPUT_ERROR, "~ExtensionCommand %s\n", m_commandName);
    }

    virtual bool
    DoExecute (lldb::SBDebugger debugger,
               char** arguments,
               lldb::SBCommandReturnObject &result)
    {
        IHostServices* hostservices = GetHostServices();
        if (hostservices == nullptr)
        {
            result.SetStatus(lldb::eReturnStatusFailed);
            return false;
        }
        std::string commandLine;
        commandLine.append(m_commandName);
        commandLine.append(" ");
        if (arguments != nullptr)
        {
            for (const char* arg = *arguments; arg; arg = *(++arguments))
            {
                commandLine.append(arg);
                commandLine.append(" ");
            }
        }
        g_services->FlushCheck();
        HRESULT hr = hostservices->DispatchCommand(commandLine.c_str());
        if (hr != S_OK)
        {
            result.SetStatus(lldb::eReturnStatusFailed);
            return false;
        }
        result.SetStatus(lldb::eReturnStatusSuccessFinishResult);
        return result.Succeeded();
    }
};

HRESULT 
LLDBServices::AddCommand(
    PCSTR commandName,
    PCSTR help,
    PCSTR aliases[],
    int numberOfAliases)
{
    if (commandName == nullptr)
    {
        return E_INVALIDARG;
    }
    // Check if it is a lldb command or alias
    if (m_interpreter.CommandExists(commandName) || m_interpreter.AliasExists(commandName))
    { 
        return E_PENDING;
    }
    // Check if it one of our user commands (the above functions don't see user commands)
    if (m_commands.find(commandName) != m_commands.end())
    { 
        return E_PENDING;
    }
    ExtensionCommand* extensionCommand = new ExtensionCommand(commandName);
    lldb::SBCommand command = AddCommand(commandName, extensionCommand, help);
    if (!command.IsValid())
    {
        return E_INVALIDARG;
    }
    if (aliases != nullptr)
    {
        for (int i = 0; i < numberOfAliases; i++)
        {
            if (!m_interpreter.CommandExists(aliases[i]) && !m_interpreter.AliasExists(aliases[i]))
            {
                lldb::SBCommand alias = AddCommand(aliases[i], extensionCommand, help);
                if (!alias.IsValid())
                {
                    return E_INVALIDARG;
                }
            }
        }
    }
    return S_OK;
}

void
LLDBServices::OutputString(
    ULONG mask,
    PCSTR str)
{
    FILE* file;
    if (mask == DEBUG_OUTPUT_ERROR)
    {
        file = m_debugger.GetErrorFileHandle();
    }
    else 
    {
        file = m_debugger.GetOutputFileHandle();
    }
    fputs(str, file);
}

HRESULT 
LLDBServices::GetNumberThreads(
    PULONG number)
{
    if (number == nullptr)
    {
        return E_INVALIDARG;
    }
    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        *number = 0;
        return E_UNEXPECTED;
    }
    *number = process.GetNumThreads();
    return S_OK;
}

HRESULT 
LLDBServices::GetThreadIdsByIndex(
    ULONG start,
    ULONG count,
    PULONG ids,
    PULONG sysIds)
{
    lldb::SBProcess process = GetCurrentProcess();
    if (!process.IsValid())
    {
        return E_UNEXPECTED;
    }
    uint32_t number = process.GetNumThreads();
    if (start >= number || start + count > number)
    {
        return E_INVALIDARG;
    }
    for (int index = start; index < start + count; index++)
    {
        lldb::SBThread thread = process.GetThreadAtIndex(index);
        if (!thread.IsValid())
        {
            return E_UNEXPECTED;
        }
        if (ids != nullptr)
        {
            ids[index] = thread.GetIndexID();
        }
        if (sysIds != nullptr)
        {
            sysIds[index] = GetThreadId(thread);
        }
    }
    return S_OK;
}

HRESULT 
LLDBServices::SetCurrentThreadSystemId(
    ULONG sysId)
{
    lldb::SBThread thread = GetThreadBySystemId(sysId);
    if (!thread.IsValid())
    {
        return E_FAIL;
    }
    if (!thread.GetProcess().SetSelectedThread(thread))
    {
        return E_FAIL;
    }
    return S_OK;
}

HRESULT 
LLDBServices::GetThreadTeb(
    ULONG sysId,
    PULONG64 pteb)
{
    return E_NOTIMPL;
}

HRESULT 
LLDBServices::GetSymbolPath(
    PSTR buffer,
    ULONG bufferSize,
    PULONG pathSize)
{
    return E_NOTIMPL;
}
 
HRESULT 
LLDBServices::GetSymbolByOffset(
    ULONG moduleIndex,
    ULONG64 offset,
    PSTR nameBuffer,
    ULONG nameBufferSize,
    PULONG nameSize,
    PULONG64 displacement)
{
    return GetNameByOffset(moduleIndex, offset, nameBuffer, nameBufferSize, nameSize, displacement);
}

HRESULT 
LLDBServices::GetOffsetBySymbol(
    ULONG moduleIndex,
    PCSTR name,
    PULONG64 offset)
{
    HRESULT hr = S_OK;

    lldb::SBTarget target;
    lldb::SBModule module;
    lldb::SBSymbol symbol;
    lldb::SBAddress startAddress;

    if (offset == nullptr)
    {
        hr = E_INVALIDARG;
        goto exit;
    }

    target = m_debugger.GetSelectedTarget();
    if (!target.IsValid())
    {
        hr = E_FAIL;
        goto exit;
    }

    module = target.GetModuleAtIndex(moduleIndex);
    if (!module.IsValid())
    {
        hr = E_INVALIDARG;
        goto exit;
    }

    symbol = module.FindSymbol(name);
    if (!symbol.IsValid())
    {
        hr = E_INVALIDARG;
        goto exit;
    }
    startAddress = symbol.GetStartAddress();
    *offset = startAddress.GetLoadAddress(target);
exit:
    return hr;
}

//----------------------------------------------------------------------------
// Helper functions
//----------------------------------------------------------------------------

void
LLDBServices::InitializeThreadInfo(lldb::SBProcess process)
{
#ifdef __APPLE__
    if (m_threadInfoInitialized)
    {
        return;
    }
    m_threadInfoInitialized = true;

    // Only attempt to read the special thread info block if MacOS core dump
    const char* pluginName = process.GetPluginName();
    if (strcmp(pluginName, "mach-o-core") != 0)
    {
        return;
    }
    SpecialThreadInfoHeader header;
    lldb::SBError error;
    size_t read = process.ReadMemory(SpecialThreadInfoAddress, &header, sizeof(SpecialThreadInfoHeader), error);
    if (error.Fail() || read != sizeof(header))
    {
        return;
    }
    if (strncmp(header.signature, SPECIAL_THREADINFO_SIGNATURE, sizeof(SPECIAL_THREADINFO_SIGNATURE)) != 0)
    {
        Output(DEBUG_OUTPUT_WARNING, "Special thread info signature invalid\n");
        return;
    }
    uint32_t number = process.GetNumThreads();
    if (number != header.numThreads)
    {
        Output(DEBUG_OUTPUT_WARNING, "Special thread info number of threads mismatched - lldb: %d header: %d\n", number, header.numThreads);
        return;
    }
    m_processId = header.pid;
    m_threadInfos.clear();

    uint64_t address = SpecialThreadInfoAddress + sizeof(header);
    for (int index = 0; index < number; index++)
    {
        SpecialThreadInfoEntry entry;
        read = process.ReadMemory(address, &entry, sizeof(SpecialThreadInfoEntry), error);
        if (error.Fail() || read != sizeof(entry)) {
            Output(DEBUG_OUTPUT_WARNING, "Special thread info entry %d read failed\n", index);
            break;
        }
        m_threadInfos.push_back(entry);
        address += sizeof(SpecialThreadInfoEntry);

        // Validate that the thread stack pointer matches the thread info's.
        lldb::SBThread thread = process.GetThreadAtIndex(index);
        if (thread.IsValid())
        {
            lldb::SBFrame frame = thread.GetFrameAtIndex(0);
            if (frame.IsValid())
            {
                if (frame.GetSP() != entry.sp)
                {
                    Output(DEBUG_OUTPUT_WARNING, "Special thread info SP (%p) doesn't match %p\n", (void*)entry.sp, (void*)frame.GetSP());
                }
            }
            else
            {
                Output(DEBUG_OUTPUT_WARNING, "Invalid stack frame for thread %d\n", index);
            }
        }
        else
        {
            Output(DEBUG_OUTPUT_WARNING, "Invalid thread %d\n", index);
        }
    }
#endif
}

lldb::SBThread 
LLDBServices::GetThreadBySystemId(
    ULONG sysId)
{
    lldb::SBProcess process;
    lldb::SBThread thread;

    if (sysId == 0)
    {
        goto exit;
    }

    process = GetCurrentProcess();
    if (!process.IsValid())
    {
        goto exit;
    }

    for (int index = 0; index < process.GetNumThreads(); index++)
    {
        if (m_threadInfos.size() <= index)
        {
            break;
        }
        if (sysId == m_threadInfos[index].tid)
        {
            thread = process.GetThreadAtIndex(index);
            goto exit;
        }
    }

    thread = process.GetThreadByID(sysId);

exit:
    return thread;
}

void 
LLDBServices::AddThreadInfoEntry(uint32_t tid, uint32_t index)
{
    // Make sure there is room in the thread infos vector
    if (m_threadInfos.empty())
    {
        uint32_t number;
        GetNumberThreads(&number);
        m_threadInfos.assign(number, SpecialThreadInfoEntry{ 0, 0 });
    }
    m_threadInfos[index - 1] = SpecialThreadInfoEntry{ tid, 0 };
}

uint32_t 
LLDBServices::GetProcessId(lldb::SBProcess process)
{
    return m_processId != 0 ? m_processId : process.GetProcessID();
}

uint32_t 
LLDBServices::GetThreadId(lldb::SBThread thread)
{
    uint32_t index = thread.GetIndexID() - 1;
    if (m_threadInfos.size() > index && m_threadInfos[index].tid != 0)
    {
        return m_threadInfos[index].tid;
    }
    else
    {
        return thread.GetThreadID();
    }
}

lldb::SBProcess
LLDBServices::GetCurrentProcess()
{
    lldb::SBProcess process;

    if (m_currentProcess == nullptr)
    {
        lldb::SBTarget target = m_debugger.GetSelectedTarget();
        if (target.IsValid())
        {
            process = target.GetProcess();
        }
    }
    else
    {
        process = *m_currentProcess;
    }

    return process;
}

lldb::SBThread 
LLDBServices::GetCurrentThread()
{
    lldb::SBThread thread;

    if (m_currentThread == nullptr)
    {
        lldb::SBProcess process = GetCurrentProcess();
        if (process.IsValid())
        {
            thread = process.GetSelectedThread();
        }
    }
    else
    {
        thread = *m_currentThread;
    }

    return thread;
}

lldb::SBFrame 
LLDBServices::GetCurrentFrame()
{
    lldb::SBFrame frame;

    lldb::SBThread thread = GetCurrentThread();
    if (thread.IsValid())
    {
        frame = thread.GetSelectedFrame();
    }

    return frame;
}

void 
DummyFunction()
{
}

PCSTR
LLDBServices::GetPluginModuleDirectory()
{
    if (g_pluginModuleDirectory == nullptr)
    {
        Dl_info info;
        if (dladdr((void *)&DummyFunction, &info) != 0)
        {
            std::string path(info.dli_fname);

            // Parse off the module name to get just the path
            size_t lastSlash = path.rfind('/');
            if (lastSlash != std::string::npos)
            {
                path.erase(lastSlash);
                path.append("/");
                g_pluginModuleDirectory = strdup(path.c_str());
            }
        }
    }
    return g_pluginModuleDirectory;
}

bool
LLDBServices::GetVersionStringFromSection(lldb::SBTarget& target, lldb::SBSection& section, char* versionBuffer)
{
    if (section.IsValid())
    {
        lldb::SectionType sectionType = section.GetSectionType();

        if (sectionType == lldb::eSectionTypeContainer)
        {
            int numSubSections = section.GetNumSubSections();
            for (int subsi = 0; subsi < numSubSections; subsi++)
            {
                lldb::SBSection subSection = section.GetSubSectionAtIndex(subsi);
                if (GetVersionStringFromSection(target, subSection, versionBuffer)) {
                    return true;
                }
            }
        }
        else if (sectionType == lldb::eSectionTypeData)
        {
            lldb::addr_t address = section.GetLoadAddress(target);
            uint32_t size = section.GetByteSize();
            if (SearchVersionString(address, size, versionBuffer, VersionBufferSize)) {
                return true;
            }
        }
    }
    return false;
}

#define VersionLength 12
static const char* g_versionString = "@(#)Version ";

bool 
LLDBServices::SearchVersionString(
    uint64_t address, 
    int32_t size,
    char* versionBuffer,
    int versionBufferSize)
{
    BYTE buffer[VersionLength];
    ULONG cbBytesRead;
    bool result;

    ClearCache();

    while (size > 0) 
    {
        result = ReadVirtualCache(address, buffer, VersionLength, &cbBytesRead);
        if (result && cbBytesRead >= VersionLength)
        {
            if (memcmp(buffer, g_versionString, VersionLength) == 0)
            {
                for (int i = 0; i < versionBufferSize; i++)
                {
                    // Now read the version string a char/byte at a time
                    result = ReadVirtualCache(address, &versionBuffer[i], 1, &cbBytesRead);

                    // Return not found if there are any failures or problems while reading the version string.
                    if (!result || cbBytesRead < 1 || size <= 0) {
                        break;
                    }
                    // Found the end of the string
                    if (versionBuffer[i] == '\0') {
                        return true;
                    }
                    address++;
                    size--;
                }
                // Return not found if overflowed the versionBuffer (not finding a null).
                break;
            }
            address++;
            size--;
        }
        else
        {
            address += VersionLength;
            size -= VersionLength;
        }
    }

    return false;
}

bool
LLDBServices::ReadVirtualCache(ULONG64 address, PVOID buffer, ULONG bufferSize, PULONG pcbBytesRead)
{
    if (bufferSize == 0)
    {
        return true;
    }

    if (bufferSize > CACHE_SIZE)
    {
        // Don't even try with the cache
        return ReadVirtual(address, buffer, bufferSize, pcbBytesRead) == S_OK;
    }

    if (!m_cacheValid || (address < m_startCache) || (address > (m_startCache + m_cacheSize - bufferSize)))
    {
        ULONG cbBytesRead = 0;

        m_cacheValid = false;
        m_startCache = address;

        // Avoid an int overflow
        if (m_startCache + CACHE_SIZE < m_startCache)
            m_startCache = (ULONG64)(-CACHE_SIZE);

        HRESULT hr = ReadVirtual(m_startCache, m_cache, CACHE_SIZE, &cbBytesRead);
        if (hr != S_OK)
        {
            return false;
        }

        m_cacheSize = cbBytesRead;
        m_cacheValid = true;
    }

    // If the address is within the cache, copy the cached memory to the input buffer
    LONG_PTR cacheOffset = address - m_startCache;
    if (cacheOffset >= 0 && cacheOffset < CACHE_SIZE)
    {
        int size = std::min(bufferSize, m_cacheSize);
        memcpy(buffer, (LPVOID)(m_cache + cacheOffset), size);

        if (pcbBytesRead != NULL)
        {
            *pcbBytesRead = size;
        }
    }
    else
    {
        return false;
    }

    return true;
}

void
LLDBServices::FlushCheck()
{
    // The infrastructure expects a target to only be created if there is a valid process.
    lldb::SBProcess process = GetCurrentProcess();
    if (process.IsValid())
    {
        InitializeThreadInfo(process);

        // Has the process changed since the last commmand?
        Extensions::GetInstance()->UpdateTarget(GetProcessId(process));

        // Has the target "moved" (been continued) since the last command? Flush the target.
        uint32_t stopId = process.GetStopID();
        if (stopId != m_currentStopId)
        {
            m_currentStopId = stopId;
            Extensions::GetInstance()->FlushTarget();
        }
    }
    else 
    {
        Extensions::GetInstance()->DestroyTarget();
        m_threadInfoInitialized = false;
        m_processId = 0;
    }
}

lldb::SBCommand
LLDBServices::AddCommand(
    const char* name,
    lldb::SBCommandPluginInterface* impl,
    const char* help)
{
    lldb::SBCommand command = m_interpreter.AddCommand(name, impl, help);
    if (command.IsValid())
    {
        m_commands.insert(name);
    }
    return command;
}

HRESULT 
LLDBServices::InternalOutputVaList(
    ULONG mask,
    PCSTR format,
    va_list args)
{
    HRESULT result = S_OK;
    char str[1024];

    // Try and format our string into a fixed buffer first and see if it fits
    size_t length = ::vsnprintf(str, sizeof(str), format, args);
    if (length < sizeof(str))
    {
        OutputString(mask, str);
    }
    else
    {
        // Our stack buffer wasn't big enough to contain the entire formatted
        // string, so lets let vasprintf create the string for us!
        char *str_ptr = nullptr;
        length = ::vasprintf(&str_ptr, format, args);
        if (str_ptr)
        {
            OutputString(mask, str_ptr);
            ::free (str_ptr);
        }
        else
        {
            result = E_FAIL;
        }
    }
    return result;
}
