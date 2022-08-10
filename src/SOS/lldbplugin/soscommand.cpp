// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include "sosplugin.h"
#include <dlfcn.h>
#include <string.h>
#include <string>

void *g_sosHandle = nullptr;

// If true, use the directory that libsosplugin is in to load 
// libsos, otherwise (if false) use the libcoreclr module 
// directory (legacy behavior).
bool g_usePluginDirectory = true;

class sosCommand : public lldb::SBCommandPluginInterface
{
    const char *m_command;
    const char *m_arguments;

public:
    sosCommand(const char* command, const char* arguments = nullptr)
    {
        m_command = command;
        m_arguments = arguments;
    }

    ~sosCommand()
    {
        g_services->Output(DEBUG_OUTPUT_ERROR, "~sosCommand %s\n", m_command);
    }

    virtual bool
    DoExecute (lldb::SBDebugger debugger,
               char** arguments,
               lldb::SBCommandReturnObject &result)
    {
        result.SetStatus(lldb::eReturnStatusSuccessFinishResult);

        const char* sosCommand = m_command;
        if (sosCommand == nullptr)
        {
            if (arguments == nullptr || *arguments == nullptr)
            {
                sosCommand = "Help";
            }
            else
            {
                sosCommand = *arguments++;
                if (g_services->ExecuteCommand(sosCommand, arguments, result))
                {
                    return result.Succeeded();
                }
            }
        }

        LoadSos();

        if (g_sosHandle != nullptr)
        {
            CommandFunc commandFunc = (CommandFunc)dlsym(g_sosHandle, sosCommand);
            if (commandFunc)
            {
                std::string str;
                if (m_arguments)
                {
                    str.append(m_arguments);
                    str.append(" ");
                }
                if (arguments != nullptr)
                {
                    for (const char* arg = *arguments; arg; arg = *(++arguments))
                    {
                        str.append(arg);
                        str.append(" ");
                    }
                }
                g_services->FlushCheck();
                const char* sosArgs = str.c_str();
                HRESULT hr = commandFunc(g_services, sosArgs);
                if (hr != S_OK)
                {
                    result.SetStatus(lldb::eReturnStatusFailed);
                    g_services->Output(DEBUG_OUTPUT_ERROR, "%s %s failed\n", sosCommand, sosArgs);
                }
            }
            else
            {
                result.SetStatus(lldb::eReturnStatusFailed);
                g_services->Output(DEBUG_OUTPUT_ERROR, "SOS command '%s' not found %s\n", sosCommand, dlerror());
            }
        }

        return result.Succeeded();
    }

    void
    LoadSos()
    {
        if (g_sosHandle == nullptr)
        {
            if (g_usePluginDirectory)
            {
                const char *loadDirectory = g_services->GetPluginModuleDirectory();
                if (loadDirectory != nullptr)
                {
                    g_sosHandle = LoadModule(loadDirectory, MAKEDLLNAME_A("sos"));
                    if (g_sosHandle != nullptr)
                    {
                        InitializeFunc initializeFunc = (InitializeFunc)dlsym(g_sosHandle, SOSInitialize);
                        if (initializeFunc)
                        {
                            HRESULT hr = initializeFunc(GetHost(), GetDebuggerServices());
                            if (hr != S_OK)
                            {
                                g_services->Output(DEBUG_OUTPUT_ERROR, SOSInitialize " failed %08x\n", hr);
                            }
                        }
                    }
                }
            }
            else
            {
                const char *loadDirectory = g_services->GetCoreClrDirectory();
                if (loadDirectory != nullptr)
                {
                    // Load the DAC module first explicitly because SOS and DBI
                    // have implicit references to the DAC's PAL.
                    LoadModule(loadDirectory, MAKEDLLNAME_A("mscordaccore"));

                    g_sosHandle = LoadModule(loadDirectory, MAKEDLLNAME_A("sos"));
                }
            }
        }
    }

    void *
    LoadModule(const char *loadDirectory, const char *moduleName)
    {
        std::string modulePath(loadDirectory);
        modulePath.append(moduleName);

        void *moduleHandle = dlopen(modulePath.c_str(), RTLD_NOW);
        if (moduleHandle == nullptr)
        {
            g_services->Output(DEBUG_OUTPUT_ERROR, "Could not load '%s' - %s\n", modulePath.c_str(), dlerror());
        }

        return moduleHandle;
    }
};

bool
sosCommandInitialize(lldb::SBDebugger debugger)
{
    g_services->AddCommand("sos", new sosCommand(nullptr), "Various .NET Core debugging commands. See 'soshelp' for more details. sos <command-name> <args>");
    g_services->AddCommand("ext", new sosCommand(nullptr), "Various .NET Core debugging commands. See 'soshelp' for more details. ext <command-name> <args>");
    g_services->AddCommand("bpmd", new sosCommand("bpmd"), "Creates a breakpoint at the specified managed method in the specified module.");
    g_services->AddManagedCommand("clrmodules", "Lists the managed modules in the process.");
    g_services->AddCommand("clrstack", new sosCommand("ClrStack"), "Provides a stack trace of managed code only.");
    g_services->AddCommand("clrthreads", new sosCommand("Threads"), "List the managed threads running.");
    g_services->AddCommand("clru", new sosCommand("u"), "Displays an annotated disassembly of a managed method.");
    g_services->AddCommand("dbgout", new sosCommand("dbgout"), "Enable/disable (-off) internal SOS logging.");
    g_services->AddCommand("dumpalc", new sosCommand("DumpALC"), "Displays details about a collectible AssemblyLoadContext to which the specified object is loaded.");
    g_services->AddCommand("dumparray", new sosCommand("DumpArray"), "Displays details about a managed array.");
    g_services->AddManagedCommand("dumpasync", "Displays information about async \"stacks\" on the garbage-collected heap.");
    g_services->AddCommand("dumpassembly", new sosCommand("DumpAssembly"), "Displays details about an assembly.");
    g_services->AddCommand("dumpclass", new sosCommand("DumpClass"), "Displays information about a EE class structure at the specified address.");
    g_services->AddCommand("dumpdelegate", new sosCommand("DumpDelegate"), "Displays information about a delegate.");
    g_services->AddCommand("dumpdomain", new sosCommand("DumpDomain"), "Displays information all the AppDomains and all assemblies within the domains.");
    g_services->AddCommand("dumpgcdata", new sosCommand("DumpGCData"), "Displays information about the GC data.");
    g_services->AddCommand("dumpheap", new sosCommand("DumpHeap"), "Displays info about the garbage-collected heap and collection statistics about objects.");
    g_services->AddCommand("dumpil", new sosCommand("DumpIL"), "Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.");
    g_services->AddCommand("dumplog", new sosCommand("DumpLog"), "Writes the contents of an in-memory stress log to the specified file.");
    g_services->AddCommand("dumpmd", new sosCommand("DumpMD"), "Displays information about a MethodDesc structure at the specified address.");
    g_services->AddCommand("dumpmodule", new sosCommand("DumpModule"), "Displays information about a EE module structure at the specified address.");
    g_services->AddCommand("dumpmt", new sosCommand("DumpMT"), "Displays information about a method table at the specified address.");
    g_services->AddCommand("dumpobj", new sosCommand("DumpObj"), "Displays info about an object at the specified address.");
    g_services->AddCommand("dumpruntimetypes", new sosCommand("DumpRuntimeTypes"), "Finds all System.RuntimeType objects in the gc heap and prints the type name and MethodTable they refer too.");
    g_services->AddCommand("dumpsig", new sosCommand("DumpSig"), "This command dumps the signature of a method or field given by <sigaddr> <moduleaddr>.");
    g_services->AddCommand("dumpsigelem", new sosCommand("DumpSigElem"), "This command dumps a single element of a signature object.");
    g_services->AddCommand("dumpstack", new sosCommand("DumpStack"), "Displays a native and managed stack trace.");
    g_services->AddCommand("dumpstackobjects", new sosCommand("DumpStackObjects"), "Displays all managed objects found within the bounds of the current stack.");
    g_services->AddCommand("dso", new sosCommand("DumpStackObjects"), "Displays all managed objects found within the bounds of the current stack.");
    g_services->AddCommand("dumpvc", new sosCommand("DumpVC"), "Displays info about the fields of a value class.");
    g_services->AddCommand("eeheap", new sosCommand("EEHeap"), "Displays info about process memory consumed by internal runtime data structures.");
    g_services->AddCommand("eestack", new sosCommand("EEStack"), "Runs dumpstack on all threads in the process.");
    g_services->AddCommand("eeversion", new sosCommand("EEVersion"), "Displays information about the runtime version.");
    g_services->AddCommand("ehinfo", new sosCommand("EHInfo"), "Displays the exception handling blocks in a jitted method.");
    g_services->AddCommand("finalizequeue", new sosCommand("FinalizeQueue"), "Displays all objects registered for finalization.");
    g_services->AddCommand("findappdomain", new sosCommand("FindAppDomain"), "Attempts to resolve the AppDomain of a GC object.");
    g_services->AddCommand("findroots", new sosCommand("FindRoots"), "");
    g_services->AddCommand("gchandles", new sosCommand("GCHandles"), "Displays statistics about garbage collector handles in the process.");
    g_services->AddCommand("gcheapstat", new sosCommand("GCHeapStat"), "Displays statistics about garbage collector.");
    g_services->AddCommand("gcinfo", new sosCommand("GCInfo"), "Displays info JIT GC encoding for a method.");
    g_services->AddCommand("gcroot", new sosCommand("GCRoot"), "Displays info about references (or roots) to an object at the specified address.");
    g_services->AddCommand("gcwhere", new sosCommand("GCWhere"), "Displays the location in the GC heap of the argument passed in.");
    g_services->AddCommand("histclear", new sosCommand("HistClear"), "Releases any resources used by the family of Hist commands.");
    g_services->AddCommand("histinit", new sosCommand("HistInit"), "Initializes the SOS structures from the stress log saved in the debuggee.");
    g_services->AddCommand("histobj", new sosCommand("HistObj"), "Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.");
    g_services->AddCommand("histobjfind", new sosCommand("HistObjFind"), "Displays all the log entries that reference an object at the specified address.");
    g_services->AddCommand("histroot", new sosCommand("HistRoot"), "Displays information related to both promotions and relocations of the specified root.");
    g_services->AddCommand("histstats", new sosCommand("HistStats"), "Displays stress log stats.");
    g_services->AddCommand("ip2md", new sosCommand("IP2MD"), "Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.");
    g_services->AddCommand("listnearobj", new sosCommand("ListNearObj"), "displays the object preceeding and succeeding the address passed.");
    g_services->AddManagedCommand("loadsymbols", "Load the .NET Core native module symbols.");
    g_services->AddManagedCommand("logging", "Enable/disable internal SOS logging.");
    g_services->AddCommand("name2ee", new sosCommand("Name2EE"), "Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.");
    g_services->AddCommand("objsize", new sosCommand("ObjSize"), "Displays the size of the specified object.");
    g_services->AddCommand("pathto", new sosCommand("PathTo"), "Displays the GC path from <root> to <target>.");
    g_services->AddCommand("pe", new sosCommand("PrintException"), "Displays and formats fields of any object derived from the Exception class at the specified address.");
    g_services->AddCommand("printexception", new sosCommand("PrintException"), "Displays and formats fields of any object derived from the Exception class at the specified address.");
    g_services->AddCommand("runtimes", new sosCommand("runtimes"), "List the runtimes in the target or change the default runtime.");
    g_services->AddCommand("stoponcatch", new sosCommand("StopOnCatch"), "Debuggee will break the next time a managed exception is caught during execution");
    g_services->AddCommand("setclrpath", new sosCommand("SetClrPath"), "Set the path to load the runtime DAC/DBI files.");
    g_services->AddManagedCommand("setsymbolserver", "Enables the symbol server support ");
    g_services->AddCommand("soshelp", new sosCommand("Help"), "Displays all available commands when no parameter is specified, or displays detailed help information about the specified command. soshelp <command>");
    g_services->AddCommand("sosstatus", new sosCommand("SOSStatus"), "Displays the global SOS status.");
    g_services->AddCommand("sosflush", new sosCommand("SOSFlush"), "Flushes the DAC caches.");
    g_services->AddCommand("syncblk", new sosCommand("SyncBlk"), "Displays the SyncBlock holder info.");
    g_services->AddCommand("threadpool", new sosCommand("ThreadPool"), "Displays info about the runtime thread pool.");
    g_services->AddCommand("threadstate", new sosCommand("ThreadState"), "Pretty prints the meaning of a threads state.");
    g_services->AddCommand("token2ee", new sosCommand("token2ee"), "Displays the MethodTable structure and MethodDesc structure for the specified token and module.");
    g_services->AddCommand("verifyheap", new sosCommand("VerifyHeap"), "Checks the GC heap for signs of corruption.");
    g_services->AddCommand("verifyobj", new sosCommand("VerifyObj"), "Checks the object that is passed as an argument for signs of corruption.");
    return true;
}
