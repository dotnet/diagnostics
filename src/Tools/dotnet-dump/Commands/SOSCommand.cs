// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;
using SOS;
using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "clrstack",         AliasExpansion = "ClrStack",            Help = "Provides a stack trace of managed code only.")]
    [Command(Name = "clrthreads",       AliasExpansion = "Threads",             Help = "List the managed threads running.")]
    [Command(Name = "dbgout",           AliasExpansion = "dbgout",              Help = "Enable/disable (-off) internal SOS logging.")]
    [Command(Name = "dumparray",        AliasExpansion = "DumpArray",           Help = "Displays details about a managed array.")]
    [Command(Name = "dumpasync",        AliasExpansion = "DumpAsync",           Help = "Displays info about async state machines on the garbage-collected heap.")]
    [Command(Name = "dumpassembly",     AliasExpansion = "DumpAssembly",        Help = "Displays details about an assembly.")]
    [Command(Name = "dumpclass",        AliasExpansion = "DumpClass",           Help = "Displays information about a EE class structure at the specified address.")]
    [Command(Name = "dumpdelegate",     AliasExpansion = "DumpDelegate",        Help = "Displays information about a delegate.")]
    [Command(Name = "dumpdomain",       AliasExpansion = "DumpDomain",          Help = "Displays information all the AppDomains and all assemblies within the domains.")]
    [Command(Name = "dumpheap",         AliasExpansion = "DumpHeap",            Help = "Displays info about the garbage-collected heap and collection statistics about objects.")]
    [Command(Name = "dumpil",           AliasExpansion = "DumpIL",              Help = "Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.")]
    [Command(Name = "dumplog",          AliasExpansion = "DumpLog",             Help = "Writes the contents of an in-memory stress log to the specified file.")]
    [Command(Name = "dumpmd",           AliasExpansion = "DumpMD",              Help = "Displays information about a MethodDesc structure at the specified address.")]
    [Command(Name = "dumpmodule",       AliasExpansion = "DumpModule",          Help = "Displays information about a EE module structure at the specified address.")]
    [Command(Name = "dumpmt",           AliasExpansion = "DumpMT",              Help = "Displays information about a method table at the specified address.")]
    [Command(Name = "dumpobj",          AliasExpansion = "DumpObj",             Help = "Displays info about an object at the specified address.")]
    [Command(Name = "dumpvc",           AliasExpansion = "DumpVC",              Help = "Displays info about the fields of a value class.")]
    [Command(Name = "dumpstackobjects", AliasExpansion = "DumpStackObjects",    Help = "Displays all managed objects found within the bounds of the current stack.")]
    [CommandAlias(Name = "dso")]
    [Command(Name = "eeheap",           AliasExpansion = "EEHeap",              Help = "Displays info about process memory consumed by internal runtime data structures.")]
    [Command(Name = "eeversion",        AliasExpansion = "EEVersion",           Help = "Displays information about the runtime version.")]
    [Command(Name = "finalizequeue",    AliasExpansion = "FinalizeQueue",       Help = "Displays all objects registered for finalization.")]
    [Command(Name = "gcroot",           AliasExpansion = "GCRoot",              Help = "Displays info about references (or roots) to an object at the specified address.")]
    [Command(Name = "gcwhere",          AliasExpansion = "GCWhere",             Help = "Displays the location in the GC heap of the argument passed in.")]
    [Command(Name = "ip2md",            AliasExpansion = "IP2MD",               Help = "Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.")]
    [Command(Name = "name2ee",          AliasExpansion = "Name2EE",             Help = "Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.")]
    [Command(Name = "printexception",   AliasExpansion = "PrintException",      Help = "Displays and formats fields of any object derived from the Exception class at the specified address.")]
    [CommandAlias(Name = "pe")]
    [Command(Name = "sosstatus",        AliasExpansion = "SOSStatus",           Help = "Displays the global SOS status.")]
    [Command(Name = "syncblk",          AliasExpansion = "SyncBlk",             Help = "Displays the SyncBlock holder info.")]
    [Command(Name = "histclear",        AliasExpansion = "HistClear",           Help = "Releases any resources used by the family of Hist commands.")]
    [Command(Name = "histinit",         AliasExpansion = "HistInit",            Help = "Initializes the SOS structures from the stress log saved in the debuggee.")]
    [Command(Name = "histobj",          AliasExpansion = "HistObj",             Help = "Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to the address passed in as an argument.")]
    [Command(Name = "histobjfind",      AliasExpansion = "HistObjFind",         Help = "Displays all the log entries that reference an object at the specified address.")]
    [Command(Name = "histroot",         AliasExpansion = "HistRoot",            Help = "Displays information related to both promotions and relocations of the specified root.")]
    [Command(Name = "setsymbolserver",  AliasExpansion = "SetSymbolServer",     Help = "Enables the symbol server support.")]
    [Command(Name = "verifyheap",       AliasExpansion = "VerifyHeap",          Help = "Checks the GC heap for signs of corruption.")]
    [Command(Name = "threadpool",       AliasExpansion = "ThreadPool",          Help = "Lists basic information about the thread pool.")]
    [Command(Name = "dumprcw",          AliasExpansion = "DumpRCW",             Platform = CommandPlatform.Windows, Help = "Displays information about a Runtime Callable Wrapper.")]
    [Command(Name = "dumpccw",          AliasExpansion = "DumpCCW",             Platform = CommandPlatform.Windows, Help = "Displays information about a COM Callable Wrapper.")]
    [Command(Name = "dumppermissionset",AliasExpansion = "DumpPermissionSet",   Platform = CommandPlatform.Windows, Help = "Displays a PermissionSet object (debug build only).")]
    [Command(Name = "traverseheap",     AliasExpansion = "TraverseHeap",        Platform = CommandPlatform.Windows, Help = "Writes out a file in a format understood by the CLR Profiler.")]
    [Command(Name = "analyzeoom",       AliasExpansion = "AnalyzeOOM",          Platform = CommandPlatform.Windows, Help = "Displays the info of the last OOM occurred on an allocation request to the GC heap.")]
    [Command(Name = "verifyobj",        AliasExpansion = "VerifyObj",           Platform = CommandPlatform.Windows, Help = "Checks the object for signs of corruption.")]
    [Command(Name = "listnearobj",      AliasExpansion = "ListNearObj",         Platform = CommandPlatform.Windows, Help = "Displays the object preceding and succeeding the address specified.")]
    [Command(Name = "gcheapstat",       AliasExpansion = "GCHeapStat",          Platform = CommandPlatform.Windows, Help = "Display various GC heap stats.")]
    [Command(Name = "watsonbuckets",    AliasExpansion = "WatsonBuckets",       Platform = CommandPlatform.Windows, Help = "Displays the Watson buckets.")]
    [Command(Name = "comstate",         AliasExpansion = "COMState",            Platform = CommandPlatform.Windows, Help = "Lists the COM apartment model for each thread.")]
    [Command(Name = "gchandles",        AliasExpansion = "GCHandles",           Platform = CommandPlatform.Windows, Help = "Provides statistics about GCHandles in the process.")]
    [Command(Name = "objsize",          AliasExpansion = "ObjSize",             Platform = CommandPlatform.Windows, Help = "Lists the sizes of the all the objects found on managed threads.")]
    [Command(Name = "gchandleleaks",    AliasExpansion = "GCHandleLeaks",       Platform = CommandPlatform.Windows, Help = "Helps in tracking down GCHandle leaks")]
    public class SOSCommand : CommandBase
    {
        [Argument(Name = "arguments", Help = "Arguments to SOS command.")]
        public string[] Arguments { get; set; }

        public SOSHost SOSHost { get; set; }

        public override void Invoke()
        {
            try {
                string arguments = null;
                if (Arguments != null && Arguments.Length > 0) {
                    arguments = string.Concat(Arguments.Select((arg) => arg + " "));
                }
                SOSHost.ExecuteCommand(AliasExpansion, arguments);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException) {
                WriteLineError(ex.Message);
            }
        }

        [HelpInvoke]
        public void InvokeHelp()
        {
            SOSHost.ExecuteCommand("Help", AliasExpansion);
        }
    }
}
