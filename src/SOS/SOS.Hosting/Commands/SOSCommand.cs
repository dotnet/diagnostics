// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SOS.Hosting
{
    [Command(Name = "clrstack", DefaultOptions = "ClrStack", Help = "Provides a stack trace of managed code only.")]
    [Command(Name = "clrthreads", DefaultOptions = "Threads", Help = "List the managed threads running.")]
    [Command(Name = "dbgout", DefaultOptions = "dbgout", Help = "Enable/disable (-off) internal SOS logging.")]
    [Command(Name = "dumpalc", DefaultOptions = "DumpALC", Help = "Displays details about a collectible AssemblyLoadContext into which the specified object is loaded.")]
    [Command(Name = "dumparray", DefaultOptions = "DumpArray", Help = "Displays details about a managed array.")]
    [Command(Name = "dumpasync", DefaultOptions = "DumpAsync", Help = "Displays info about async state machines on the garbage-collected heap.")]
    [Command(Name = "dumpassembly", DefaultOptions = "DumpAssembly", Help = "Displays details about an assembly.")]
    [Command(Name = "dumpclass", DefaultOptions = "DumpClass", Help = "Displays information about a EE class structure at the specified address.")]
    [Command(Name = "dumpdelegate", DefaultOptions = "DumpDelegate", Help = "Displays information about a delegate.")]
    [Command(Name = "dumpdomain", DefaultOptions = "DumpDomain", Help = "Displays information all the AppDomains and all assemblies within the domains.")]
    [Command(Name = "dumpheap", DefaultOptions = "DumpHeap", Help = "Displays info about the garbage-collected heap and collection statistics about objects.")]
    [Command(Name = "dumpil", DefaultOptions = "DumpIL", Help = "Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.")]
    [Command(Name = "dumplog", DefaultOptions = "DumpLog", Help = "Writes the contents of an in-memory stress log to the specified file.")]
    [Command(Name = "dumpmd", DefaultOptions = "DumpMD", Help = "Displays information about a MethodDesc structure at the specified address.")]
    [Command(Name = "dumpmodule", DefaultOptions = "DumpModule", Help = "Displays information about a EE module structure at the specified address.")]
    [Command(Name = "dumpmt", DefaultOptions = "DumpMT", Help = "Displays information about a method table at the specified address.")]
    [Command(Name = "dumpobj", DefaultOptions = "DumpObj", Aliases = new string[] { "do" }, Help = "Displays info about an object at the specified address.")]
    [Command(Name = "dumpvc", DefaultOptions = "DumpVC", Help = "Displays info about the fields of a value class.")]
    [Command(Name = "dumpstackobjects", DefaultOptions = "DumpStackObjects", Aliases = new string[] { "dso" }, Help = "Displays all managed objects found within the bounds of the current stack.")]
    [Command(Name = "eeheap", DefaultOptions = "EEHeap", Help = "Displays info about process memory consumed by internal runtime data structures.")]
    [Command(Name = "eeversion", DefaultOptions = "EEVersion", Help = "Displays information about the runtime version.")]
    [Command(Name = "finalizequeue", DefaultOptions = "FinalizeQueue", Help = "Displays all objects registered for finalization.")]
    [Command(Name = "gcroot", DefaultOptions = "GCRoot", Help = "Displays info about references (or roots) to an object at the specified address.")]
    [Command(Name = "gcwhere", DefaultOptions = "GCWhere", Help = "Displays the location in the GC heap of the argument passed in.")]
    [Command(Name = "ip2md", DefaultOptions = "IP2MD", Help = "Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.")]
    [Command(Name = "name2ee", DefaultOptions = "Name2EE", Help = "Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.")]
    [Command(Name = "printexception", DefaultOptions = "PrintException", Aliases = new string[] { "pe" }, Help = "Displays and formats fields of any object derived from the Exception class at the specified address.")]
    [Command(Name = "syncblk", DefaultOptions = "SyncBlk", Help = "Displays the SyncBlock holder info.")]
    [Command(Name = "histclear", DefaultOptions = "HistClear", Help = "Releases any resources used by the family of Hist commands.")]
    [Command(Name = "histinit", DefaultOptions = "HistInit", Help = "Initializes the SOS structures from the stress log saved in the debuggee.")]
    [Command(Name = "histobj", DefaultOptions = "HistObj", Help = "Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.")]
    [Command(Name = "histobjfind", DefaultOptions = "HistObjFind", Help = "Displays all the log entries that reference an object at the specified address.")]
    [Command(Name = "histroot", DefaultOptions = "HistRoot", Help = "Displays information related to both promotions and relocations of the specified root.")]
    [Command(Name = "setsymbolserver", DefaultOptions = "SetSymbolServer", Help = "Enables the symbol server support.")]
    [Command(Name = "verifyheap", DefaultOptions = "VerifyHeap", Help = "Checks the GC heap for signs of corruption.")]
    [Command(Name = "threadpool", DefaultOptions = "ThreadPool", Help = "Lists basic information about the thread pool.")]
    [Command(Name = "soshelp", DefaultOptions = "Help", Help = "Displays help for a specific SOS command.")]
    [Command(Name = "dumprcw", DefaultOptions = "DumpRCW", Platform = CommandPlatform.Windows, Help = "Displays information about a Runtime Callable Wrapper.")]
    [Command(Name = "dumpccw", DefaultOptions = "DumpCCW", Platform = CommandPlatform.Windows, Help = "Displays information about a COM Callable Wrapper.")]
    [Command(Name = "dumppermissionset", DefaultOptions = "DumpPermissionSet", Platform = CommandPlatform.Windows, Help = "Displays a PermissionSet object (debug build only).")]
    [Command(Name = "traverseheap", DefaultOptions = "TraverseHeap", Platform = CommandPlatform.Windows, Help = "Writes out a file in a format understood by the CLR Profiler.")]
    [Command(Name = "analyzeoom", DefaultOptions = "AnalyzeOOM", Platform = CommandPlatform.Windows, Help = "Displays the info of the last OOM occurred on an allocation request to the GC heap.")]
    [Command(Name = "verifyobj", DefaultOptions = "VerifyObj", Platform = CommandPlatform.Windows, Help = "Checks the object for signs of corruption.")]
    [Command(Name = "listnearobj", DefaultOptions = "ListNearObj", Platform = CommandPlatform.Windows, Help = "Displays the object preceding and succeeding the address specified.")]
    [Command(Name = "gcheapstat", DefaultOptions = "GCHeapStat", Platform = CommandPlatform.Windows, Help = "Display various GC heap stats.")]
    [Command(Name = "watsonbuckets", DefaultOptions = "WatsonBuckets", Platform = CommandPlatform.Windows, Help = "Displays the Watson buckets.")]
    [Command(Name = "comstate", DefaultOptions = "COMState", Platform = CommandPlatform.Windows, Help = "Lists the COM apartment model for each thread.")]
    [Command(Name = "gchandles", DefaultOptions = "GCHandles", Platform = CommandPlatform.Windows, Help = "Provides statistics about GCHandles in the process.")]
    [Command(Name = "objsize", DefaultOptions = "ObjSize", Platform = CommandPlatform.Windows, Help = "Lists the sizes of the all the objects found on managed threads.")]
    [Command(Name = "gchandleleaks", DefaultOptions = "GCHandleLeaks", Platform = CommandPlatform.Windows, Help = "Helps in tracking down GCHandle leaks")]
    public class SOSCommand : CommandBase
    {
        [Argument(Name = "arguments", Help = "Arguments to SOS command.")]
        public string[] Arguments { get; set; }

        public SOSHost SOSHost { get; set; }

        public override void Invoke()
        {
            try
            {
                Debug.Assert(Arguments != null && Arguments.Length > 0);
                string arguments = string.Concat(Arguments.Skip(1).Select((arg) => arg + " "));
                SOSHost.ExecuteCommand(Arguments[0], arguments);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException)
            {
                WriteLineError(ex.Message);
            }
        }

        [HelpInvoke]
        public void InvokeHelp()
        {
            SOSHost.ExecuteCommand("Help", Arguments[0]);
        }
    }
}
