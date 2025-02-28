// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <cstdarg>
#include <string>
#include <set>

#define CACHE_SIZE  4096

class LLDBServices : public ILLDBServices, public ILLDBServices2, public IDebuggerServices
{
private:
    LONG m_ref;
    lldb::SBDebugger m_debugger;
    lldb::SBCommandInterpreter m_interpreter;
    lldb::SBProcess *m_currentProcess;
    lldb::SBThread *m_currentThread;
    uint32_t m_currentStopId;
    uint32_t m_processId;
    std::set<std::string> m_commands;
    std::vector<SpecialThreadInfoEntry> m_threadInfos;
    bool m_threadInfoInitialized;

    BYTE m_cache[CACHE_SIZE];
    ULONG64 m_startCache;
    bool m_cacheValid;
    ULONG m_cacheSize;

    ULONG64 GetModuleBase(lldb::SBTarget& target, lldb::SBModule& module);
    ULONG64 GetModuleSize(ULONG64 baseAddress, lldb::SBModule& module);
    ULONG64 GetExpression(lldb::SBFrame& frame, lldb::SBError& error, PCSTR exp);
    void GetContextFromFrame(lldb::SBFrame& frame, DT_CONTEXT *dtcontext);
    DWORD_PTR GetRegister(lldb::SBFrame& frame, const char *name);

    bool GetVersionStringFromSection(lldb::SBTarget& target, lldb::SBSection& section, char* versionBuffer);
    bool SearchVersionString(uint64_t address, int32_t size, char* versionBuffer, int versionBufferSize);
    bool ReadVirtualCache(ULONG64 address, PVOID buffer, ULONG bufferSize, PULONG pcbBytesRead);

    void ClearCache()
    { 
        m_cacheValid = false;
        m_cacheSize = CACHE_SIZE;
    }

    void LoadNativeSymbols(lldb::SBTarget target, lldb::SBModule module, PFN_MODULE_LOAD_CALLBACK callback);

    void InitializeThreadInfo(lldb::SBProcess process);
    uint32_t GetProcessId(lldb::SBProcess process);
    uint32_t GetThreadId(lldb::SBThread thread);
    lldb::SBThread GetThreadBySystemId(ULONG sysId);
    lldb::SBProcess GetCurrentProcess();
    lldb::SBThread GetCurrentThread();
    lldb::SBFrame GetCurrentFrame();

public:
    LLDBServices(lldb::SBDebugger debugger);
    ~LLDBServices();
 
    std::vector<SpecialThreadInfoEntry>& ThreadInfos() { return m_threadInfos; }

    void AddThreadInfoEntry(uint32_t tid, uint32_t index);

    lldb::SBProcess* SetCurrentProcess(lldb::SBProcess* process)
    {
        return (lldb::SBProcess*)InterlockedExchangePointer(&m_currentProcess, process);
    }

    lldb::SBThread* SetCurrentThread(lldb::SBThread* thread) 
    { 
        return (lldb::SBThread*)InterlockedExchangePointer(&m_currentThread, thread);
    }

    //----------------------------------------------------------------------------
    // IUnknown
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID InterfaceId,
        PVOID* Interface);

    ULONG STDMETHODCALLTYPE AddRef();

    ULONG STDMETHODCALLTYPE Release();

    //----------------------------------------------------------------------------
    // ILLDBServices
    //----------------------------------------------------------------------------

    PCSTR STDMETHODCALLTYPE GetCoreClrDirectory();

    ULONG64 STDMETHODCALLTYPE GetExpression(
        PCSTR exp);

    HRESULT STDMETHODCALLTYPE VirtualUnwind(
        DWORD threadId,
        ULONG32 contextSize,
        PBYTE context);

    HRESULT STDMETHODCALLTYPE SetExceptionCallback(
        PFN_EXCEPTION_CALLBACK callback);

    HRESULT STDMETHODCALLTYPE ClearExceptionCallback();

    //----------------------------------------------------------------------------
    // IDebugControl2
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetInterrupt();

    HRESULT STDMETHODCALLTYPE Output(
        ULONG mask,
        PCSTR format,
        ...);

    HRESULT STDMETHODCALLTYPE OutputVaList(
        ULONG mask,
        PCSTR format,
        va_list args);

    HRESULT STDMETHODCALLTYPE ControlledOutput(
        ULONG outputControl,
        ULONG mask,
        PCSTR format,
        ...);

    HRESULT STDMETHODCALLTYPE ControlledOutputVaList(
        ULONG outputControl,
        ULONG mask,
        PCSTR format,
        va_list args);

    HRESULT STDMETHODCALLTYPE GetDebuggeeType(
        PULONG debugClass,
        PULONG qualifier);

    HRESULT STDMETHODCALLTYPE GetPageSize(
        PULONG size);

    HRESULT STDMETHODCALLTYPE GetProcessorType(
        PULONG type);

    HRESULT STDMETHODCALLTYPE Execute(
        ULONG outputControl,
        PCSTR command,
        ULONG flags);

    HRESULT STDMETHODCALLTYPE GetLastEventInformation(
        PULONG type,
        PULONG processId,
        PULONG threadId,
        PVOID extraInformation,
        ULONG extraInformationSize,
        PULONG extraInformationUsed,
        PSTR description,
        ULONG descriptionSize,
        PULONG descriptionUsed);

    HRESULT STDMETHODCALLTYPE Disassemble(
        ULONG64 offset,
        ULONG flags,
        PSTR buffer,
        ULONG bufferSize,
        PULONG disassemblySize,
        PULONG64 endOffset);

    //----------------------------------------------------------------------------
    // IDebugControl4
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetContextStackTrace(
        PVOID startContext,
        ULONG startContextSize,
        PDEBUG_STACK_FRAME frames,
        ULONG framesSize,
        PVOID frameContexts,
        ULONG frameContextsSize,
        ULONG frameContextsEntrySize,
        PULONG framesFilled);
    
    //----------------------------------------------------------------------------
    // IDebugDataSpaces
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE ReadVirtual(
        ULONG64 offset,
        PVOID buffer,
        ULONG bufferSize,
        PULONG bytesRead);

    HRESULT STDMETHODCALLTYPE WriteVirtual(
        ULONG64 offset,
        PVOID buffer,
        ULONG bufferSize,
        PULONG bytesWritten);

    //----------------------------------------------------------------------------
    // IDebugSymbols
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetSymbolOptions(
        PULONG options);

    HRESULT STDMETHODCALLTYPE GetNameByOffset(
        ULONG64 offset,
        PSTR nameBuffer,
        ULONG nameBufferSize,
        PULONG nameSize,
        PULONG64 displacement);

    HRESULT STDMETHODCALLTYPE GetNameByOffset(
        ULONG moduleIndex,
        ULONG64 offset,
        PSTR nameBuffer,
        ULONG nameBufferSize,
        PULONG nameSize,
        PULONG64 displacement);

    HRESULT STDMETHODCALLTYPE GetNumberModules(
        PULONG loaded,
        PULONG unloaded);

    HRESULT STDMETHODCALLTYPE GetModuleByIndex(
        ULONG index,
        PULONG64 base);

    HRESULT STDMETHODCALLTYPE GetModuleByModuleName(
        PCSTR name,
        ULONG startIndex,
        PULONG index,
        PULONG64 base);

    HRESULT STDMETHODCALLTYPE GetModuleByOffset(
        ULONG64 offset,
        ULONG startIndex,
        PULONG index,
        PULONG64 base);

    HRESULT STDMETHODCALLTYPE GetModuleNames(
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
        PULONG loadedImageNameSize);

    HRESULT STDMETHODCALLTYPE GetLineByOffset(
        ULONG64 offset,
        PULONG line,
        PSTR fileBuffer,
        ULONG fileBufferSize,
        PULONG fileSize,
        PULONG64 displacement);
     
    HRESULT STDMETHODCALLTYPE GetSourceFileLineOffsets(
        PCSTR file,
        PULONG64 buffer,
        ULONG bufferLines,
        PULONG fileLines);

    HRESULT STDMETHODCALLTYPE FindSourceFile(
        ULONG startElement,
        PCSTR file,
        ULONG flags,
        PULONG foundElement,
        PSTR buffer,
        ULONG bufferSize,
        PULONG foundSize);

    //----------------------------------------------------------------------------
    // IDebugSystemObjects
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetCurrentProcessSystemId(
        PULONG sysId);

    HRESULT STDMETHODCALLTYPE GetCurrentThreadId(
        PULONG id);

    HRESULT STDMETHODCALLTYPE SetCurrentThreadId(
        ULONG id);

    HRESULT STDMETHODCALLTYPE GetCurrentThreadSystemId(
        PULONG sysId);

    HRESULT STDMETHODCALLTYPE GetThreadIdBySystemId(
        ULONG sysId,
        PULONG threadId);

    HRESULT STDMETHODCALLTYPE GetThreadContextBySystemId(
        ULONG32 sysId,
        ULONG32 contextFlags,
        ULONG32 contextSize,
        PBYTE context);

    //----------------------------------------------------------------------------
    // IDebugRegisters
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetValueByName(
        PCSTR name,
        PDWORD_PTR debugValue);

    HRESULT STDMETHODCALLTYPE GetInstructionOffset(
        PULONG64 offset);

    HRESULT STDMETHODCALLTYPE GetStackOffset(
        PULONG64 offset);

    HRESULT STDMETHODCALLTYPE GetFrameOffset(
        PULONG64 offset);

    //----------------------------------------------------------------------------
    // ILLDBServices2
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE LoadNativeSymbols(
        bool runtimeOnly,
        PFN_MODULE_LOAD_CALLBACK callback);

    HRESULT STDMETHODCALLTYPE AddModuleSymbol(
        void* param, 
        const char* symbolFileName);

    HRESULT STDMETHODCALLTYPE GetModuleInfo(
        ULONG index,
        PULONG64 pBase,
        PULONG64 pSize,
        PULONG pTimestamp,
        PULONG pChecksum);

    HRESULT STDMETHODCALLTYPE GetModuleVersionInformation(
        ULONG index,
        ULONG64 base,
        PCSTR item,
        PVOID buffer,
        ULONG bufferSize,
        PULONG versionInfoSize);

    HRESULT STDMETHODCALLTYPE SetRuntimeLoadedCallback(
        PFN_RUNTIME_LOADED_CALLBACK callback);

    //----------------------------------------------------------------------------
    // IDebuggerServices
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetOperatingSystem(
        IDebuggerServices::OperatingSystem* operatingSystem);

    HRESULT STDMETHODCALLTYPE AddCommand(
        PCSTR command,
        PCSTR help,
        PCSTR aliases[],
        int numberOfAliases);

    void STDMETHODCALLTYPE OutputString(
        ULONG mask,
        PCSTR str);

    HRESULT STDMETHODCALLTYPE GetNumberThreads(
        PULONG number);

    HRESULT STDMETHODCALLTYPE GetThreadIdsByIndex(
        ULONG start,
        ULONG count,
        PULONG ids,
        PULONG sysIds);

    HRESULT STDMETHODCALLTYPE SetCurrentThreadSystemId(
        ULONG sysId);

    HRESULT STDMETHODCALLTYPE GetThreadTeb(
        ULONG sysId,
        PULONG64 pteb);

    HRESULT STDMETHODCALLTYPE GetSymbolPath(
        PSTR buffer,
        ULONG bufferSize,
        PULONG pathSize);

    HRESULT STDMETHODCALLTYPE GetSymbolByOffset(
        ULONG moduleIndex,
        ULONG64 offset,
        PSTR nameBuffer,
        ULONG nameBufferSize,
        PULONG nameSize,
        PULONG64 displacement);
 
    HRESULT STDMETHODCALLTYPE GetOffsetBySymbol(
        ULONG moduleIndex,
        PCSTR name,
        PULONG64 offset);
    
    HRESULT STDMETHODCALLTYPE GetTypeId(
        ULONG moduleIndex,
        PCSTR typeName,
        PULONG64 typeId); 

    HRESULT STDMETHODCALLTYPE GetFieldOffset(
        ULONG moduleIndex,
        PCSTR typeName,
        ULONG64 typeId,
        PCSTR fieldName,
        PULONG offset);

    ULONG STDMETHODCALLTYPE GetOutputWidth();

    HRESULT STDMETHODCALLTYPE SupportsDml(PULONG supported);

    void STDMETHODCALLTYPE OutputDmlString(
        ULONG mask,
        PCSTR str);

    void STDMETHODCALLTYPE FlushCheck();

    HRESULT STDMETHODCALLTYPE ExecuteHostCommand(
        PCSTR commandLine,
        PEXECUTE_COMMAND_OUTPUT_CALLBACK callback);

    HRESULT STDMETHODCALLTYPE GetDacSignatureVerificationSettings(
        BOOL* dacSignatureVerificationEnabled);

    //----------------------------------------------------------------------------
    // LLDBServices (internal)
    //----------------------------------------------------------------------------

    PCSTR GetPluginModuleDirectory();

    lldb::SBCommand AddCommand(const char *name, lldb::SBCommandPluginInterface *impl, const char *help);

    void AddManagedCommand(const char* name, const char* help);

    bool ExecuteCommand( const char* commandName, char** arguments, lldb::SBCommandReturnObject &result);

    HRESULT InternalOutputVaList(ULONG mask, PCSTR format, va_list args);
};
