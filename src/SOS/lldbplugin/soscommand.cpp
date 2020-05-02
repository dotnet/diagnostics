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

    virtual bool
    DoExecute (lldb::SBDebugger debugger,
               char** arguments,
               lldb::SBCommandReturnObject &result)
    {
        LLDBServices* services = new LLDBServices(debugger, result);
        LoadSos(services);

        if (g_sosHandle != nullptr)
        {
            const char* sosCommand = m_command;
            if (sosCommand == nullptr) 
            {
                if (arguments == nullptr || *arguments == nullptr) {
                    sosCommand = "Help";
                }
                else
                {
                    sosCommand = *arguments++;
                }
            }
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
                const char* sosArgs = str.c_str();
                HRESULT hr = commandFunc(services, sosArgs);
                if (hr != S_OK)
                {
                    services->Output(DEBUG_OUTPUT_ERROR, "%s %s failed\n", sosCommand, sosArgs);
                }
            }
            else
            {
                services->Output(DEBUG_OUTPUT_ERROR, "SOS command '%s' not found %s\n", sosCommand, dlerror());
            }
        }

        services->Release();
        return result.Succeeded();
    }

    void
    LoadSos(LLDBServices *services)
    {
        if (g_sosHandle == nullptr)
        {
            if (g_usePluginDirectory)
            {
                const char *loadDirectory = services->GetPluginModuleDirectory();
                if (loadDirectory != nullptr)
                {
                    g_sosHandle = LoadModule(services, loadDirectory, MAKEDLLNAME_A("sos"));
                }
            }
            else
            {
                const char *loadDirectory = services->GetCoreClrDirectory();
                if (loadDirectory != nullptr)
                {
                    // Load the DAC module first explicitly because SOS and DBI
                    // have implicit references to the DAC's PAL.
                    LoadModule(services, loadDirectory, MAKEDLLNAME_A("mscordaccore"));

                    g_sosHandle = LoadModule(services, loadDirectory, MAKEDLLNAME_A("sos"));
                }
            }
        }
    }

    void *
    LoadModule(LLDBServices *services, const char *loadDirectory, const char *moduleName)
    {
        std::string modulePath(loadDirectory);
        modulePath.append(moduleName);

        void *moduleHandle = dlopen(modulePath.c_str(), RTLD_NOW);
        if (moduleHandle == nullptr)
        {
            services->Output(DEBUG_OUTPUT_ERROR, "Could not load '%s' - %s\n", modulePath.c_str(), dlerror());
        }

        return moduleHandle;
    }
};

bool
sosCommandInitialize(lldb::SBDebugger debugger)
{
    lldb::SBCommandInterpreter interpreter = debugger.GetCommandInterpreter();
    interpreter.AddCommand("sos", new sosCommand(nullptr), "Various .NET Core debugging commands. See 'soshelp' for more details. sos <command-name> <args>");
    interpreter.AddCommand("bpmd", new sosCommand("bpmd"), "Creates a breakpoint at the specified managed method in the specified module.");
    interpreter.AddCommand("clrstack", new sosCommand("ClrStack"), "Provides a stack trace of managed code only.");
    interpreter.AddCommand("clrthreads", new sosCommand("Threads"), "List the managed threads running.");
    interpreter.AddCommand("clru", new sosCommand("u"), "Displays an annotated disassembly of a managed method.");
    interpreter.AddCommand("dbgout", new sosCommand("dbgout"), "Enable/disable (-off) internal SOS logging.");
    interpreter.AddCommand("dumparray", new sosCommand("DumpArray"), "Displays details about a managed array.");
    interpreter.AddCommand("dumpasync", new sosCommand("DumpAsync"), "Displays info about async state machines on the garbage-collected heap.");
    interpreter.AddCommand("dumpassembly", new sosCommand("DumpAssembly"), "Displays details about an assembly.");
    interpreter.AddCommand("dumpclass", new sosCommand("DumpClass"), "Displays information about a EE class structure at the specified address.");
    interpreter.AddCommand("dumpdelegate", new sosCommand("DumpDelegate"), "Displays information about a delegate.");
    interpreter.AddCommand("dumpdomain", new sosCommand("DumpDomain"), "Displays information all the AppDomains and all assemblies within the domains.");
    interpreter.AddCommand("dumpheap", new sosCommand("DumpHeap"), "Displays info about the garbage-collected heap and collection statistics about objects.");
    interpreter.AddCommand("dumpil", new sosCommand("DumpIL"), "Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.");
    interpreter.AddCommand("dumplog", new sosCommand("DumpLog"), "Writes the contents of an in-memory stress log to the specified file.");
    interpreter.AddCommand("dumpmd", new sosCommand("DumpMD"), "Displays information about a MethodDesc structure at the specified address.");
    interpreter.AddCommand("dumpmodule", new sosCommand("DumpModule"), "Displays information about a EE module structure at the specified address.");
    interpreter.AddCommand("dumpmt", new sosCommand("DumpMT"), "Displays information about a method table at the specified address.");
    interpreter.AddCommand("dumpobj", new sosCommand("DumpObj"), "Displays info about an object at the specified address.");
    interpreter.AddCommand("dumpvc", new sosCommand("DumpVC"), "Displays info about the fields of a value class.");
    interpreter.AddCommand("dumpstack", new sosCommand("DumpStack"), "Displays a native and managed stack trace.");
    interpreter.AddCommand("dso", new sosCommand("DumpStackObjects"), "Displays all managed objects found within the bounds of the current stack.");
    interpreter.AddCommand("eeheap", new sosCommand("EEHeap"), "Displays info about process memory consumed by internal runtime data structures.");
    interpreter.AddCommand("eestack", new sosCommand("EEStack"), "Runs dumpstack on all threads in the process.");
    interpreter.AddCommand("eeversion", new sosCommand("EEVersion"), "Displays information about the runtime version.");
    interpreter.AddCommand("finalizequeue", new sosCommand("FinalizeQueue"), "Displays all objects registered for finalization.");
    interpreter.AddCommand("gcroot", new sosCommand("GCRoot"), "Displays info about references (or roots) to an object at the specified address.");
    interpreter.AddCommand("gcwhere", new sosCommand("GCWhere"), "Displays the location in the GC heap of the argument passed in.");
    interpreter.AddCommand("ip2md", new sosCommand("IP2MD"), "Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.");
    interpreter.AddCommand("loadsymbols", new sosCommand("SetSymbolServer", "-loadsymbols"), "Load the .NET Core native module symbols.");
    interpreter.AddCommand("name2ee", new sosCommand("Name2EE"), "Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.");
    interpreter.AddCommand("pe", new sosCommand("PrintException"), "Displays and formats fields of any object derived from the Exception class at the specified address.");
    interpreter.AddCommand("syncblk", new sosCommand("SyncBlk"), "Displays the SyncBlock holder info.");
    interpreter.AddCommand("histclear", new sosCommand("HistClear"), "Releases any resources used by the family of Hist commands.");
    interpreter.AddCommand("histinit", new sosCommand("HistInit"), "Initializes the SOS structures from the stress log saved in the debuggee.");
    interpreter.AddCommand("histobj", new sosCommand("HistObj"), "Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.");
    interpreter.AddCommand("histobjfind", new sosCommand("HistObjFind"), "Displays all the log entries that reference an object at the specified address.");
    interpreter.AddCommand("histroot", new sosCommand("HistRoot"), "Displays information related to both promotions and relocations of the specified root.");
    interpreter.AddCommand("sethostruntime", new sosCommand("SetHostRuntime"), "Sets or displays the .NET Core runtime directory to use to run managed code in SOS.");
    interpreter.AddCommand("setclrpath", new sosCommand("SetClrPath"), "Set the path to load the runtime DAC/DBI files.");
    interpreter.AddCommand("setsymbolserver", new sosCommand("SetSymbolServer"), "Enables the symbol server support ");
    interpreter.AddCommand("sympath", new sosCommand("SetSymbolServer", "-sympath"), "Add server, cache and directory paths in the Windows symbol path format.");
    interpreter.AddCommand("soshelp", new sosCommand("Help"), "Displays all available commands when no parameter is specified, or displays detailed help information about the specified command. soshelp <command>");
    interpreter.AddCommand("sosstatus", new sosCommand("SOSStatus"), "Displays the global SOS status.");
    interpreter.AddCommand("sosflush", new sosCommand("SOSFlush"), "Flushes the DAC caches.");
    interpreter.AddCommand("threadpool", new sosCommand("ThreadPool"), "Displays info about the runtime thread pool.");
    interpreter.AddCommand("verifyheap", new sosCommand("VerifyHeap"), "Checks the GC heap for signs of corruption.");
    return true;
}
