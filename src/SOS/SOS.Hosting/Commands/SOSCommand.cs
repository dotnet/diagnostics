// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;

namespace SOS.Hosting
{
    [Command(Name = "clrstack",          DefaultOptions = "ClrStack",            Help = "Provides a stack trace of managed code only.")]
    [Command(Name = "clrthreads",        DefaultOptions = "Threads",             Help = "Lists the managed threads running.")]
    [Command(Name = "dbgout",            DefaultOptions = "dbgout",              Help = "Enables/disables (-off) internal SOS logging.")]
    [Command(Name = "dumpalc",           DefaultOptions = "DumpALC",             Help = "Displays details about a collectible AssemblyLoadContext into which the specified object is loaded.")]
    [Command(Name = "dumparray",         DefaultOptions = "DumpArray",           Help = "Displays details about a managed array.")]
    [Command(Name = "dumpassembly",      DefaultOptions = "DumpAssembly",        Help = "Displays details about an assembly.")]
    [Command(Name = "dumpclass",         DefaultOptions = "DumpClass",           Help = "Displays information about a EE class structure at the specified address.")]
    [Command(Name = "dumpdelegate",      DefaultOptions = "DumpDelegate",        Help = "Displays information about a delegate.")]
    [Command(Name = "dumpdomain",        DefaultOptions = "DumpDomain",          Help = "Displays the Microsoft intermediate language (MSIL) that's associated with a managed method.")]
    [Command(Name = "dumpgcdata",        DefaultOptions = "DumpGCData",          Help = "Displays information about the GC data.")]
    [Command(Name = "dumpil",            DefaultOptions = "DumpIL",              Help = "Displays the Microsoft intermediate language (MSIL) that is associated with a managed method.")]
    [Command(Name = "dumplog",           DefaultOptions = "DumpLog",             Help = "Writes the contents of an in-memory stress log to the specified file.")]
    [Command(Name = "dumpmd",            DefaultOptions = "DumpMD",              Help = "Displays information about a MethodDesc structure at the specified address.")]
    [Command(Name = "dumpmodule",        DefaultOptions = "DumpModule",          Help = "Displays information about a EE module structure at the specified address.")]
    [Command(Name = "dumpmt",            DefaultOptions = "DumpMT",              Help = "Displays information about a method table at the specified address.")]
    [Command(Name = "dumpobj",           DefaultOptions = "DumpObj",             Aliases = new string[] { "do" }, Help = "Displays info about an object at the specified address.")]
    [Command(Name = "dumpsig",           DefaultOptions = "DumpSig",             Help = "Dumps the signature of a method or field specified by <sigaddr> <moduleaddr>.")]
    [Command(Name = "dumpsigelem",       DefaultOptions = "DumpSigElem",         Help = "Dumps a single element of a signature object.")]
    [Command(Name = "dumpvc",            DefaultOptions = "DumpVC",              Help = "Displays info about the fields of a value class.")]
    [Command(Name = "eeversion",         DefaultOptions = "EEVersion",           Help = "Displays information about the runtime version.")]
    [Command(Name = "ehinfo",            DefaultOptions = "EHInfo",              Help = "Displays the exception handling blocks in a JIT-ed method.")]
    [Command(Name = "enummem",           DefaultOptions = "enummem",             Help = "ICLRDataEnumMemoryRegions.EnumMemoryRegions test command.")]
    [Command(Name = "findappdomain",     DefaultOptions = "FindAppDomain",       Help = "Attempts to resolve the AppDomain of a GC object.")]
    [Command(Name = "gchandles",         DefaultOptions = "GCHandles",           Help = "Provides statistics about GCHandles in the process.")]
    [Command(Name = "gcinfo",            DefaultOptions = "GCInfo",              Help = "Displays JIT GC encoding for a method.")]
    [Command(Name = "histclear",         DefaultOptions = "HistClear",           Help = "Releases any resources used by the family of Hist commands.")]
    [Command(Name = "histinit",          DefaultOptions = "HistInit",            Help = "Initializes the SOS structures from the stress log saved in the debuggee.")]
    [Command(Name = "histobj",           DefaultOptions = "HistObj",             Help = "Examines all stress log relocation records and displays the chain of garbage collection relocations that may have led to  the address passed in as an argument.")]
    [Command(Name = "histobjfind",       DefaultOptions = "HistObjFind",         Help = "Displays all the log entries that reference an object at the specified address.")]
    [Command(Name = "histroot",          DefaultOptions = "HistRoot",            Help = "Displays information related to both promotions and relocations of the specified root.")]
    [Command(Name = "histstats",         DefaultOptions = "HistStats",           Help = "Displays stress log stats.")]
    [Command(Name = "ip2md",             DefaultOptions = "IP2MD",               Help = "Displays the MethodDesc structure at the specified address in code that has been JIT-compiled.")]
    [Command(Name = "name2ee",           DefaultOptions = "Name2EE",             Help = "Displays the MethodTable structure and EEClass structure for the specified type or method in the specified module.")]
    [Command(Name = "printexception",    DefaultOptions = "PrintException",      Aliases = new string[] { "pe" }, Help = "Displays and formats fields of any object derived from the Exception class at the specified address.")]
    [Command(Name = "syncblk",           DefaultOptions = "SyncBlk",             Help = "Displays the SyncBlock holder info.")]
    [Command(Name = "threadstate",       DefaultOptions = "ThreadState",         Help = "Pretty prints the meaning of a threads state.")]
    public class SOSCommand : SOSCommandBase
    {
        [FilterInvoke]
        public static bool FilterInvoke(
            [ServiceImport(Optional = true)] ManagedOnlyCommandFilter managedOnly,
            [ServiceImport(Optional = true)] IRuntime runtime) =>
                managedOnly == null &&
                runtime != null && (runtime.RuntimeType == RuntimeType.NetCore || runtime.RuntimeType == RuntimeType.Desktop || runtime.RuntimeType == RuntimeType.SingleFile);
    }

    [Command(Name = "comstate",          DefaultOptions = "COMState",            Help = "Lists the COM apartment model for each thread.")]
    [Command(Name = "dumprcw",           DefaultOptions = "DumpRCW",             Help = "Displays information about a Runtime Callable Wrapper.")]
    [Command(Name = "dumpccw",           DefaultOptions = "DumpCCW",             Help = "Displays information about a COM Callable Wrapper.")]
    [Command(Name = "dumppermissionset", DefaultOptions = "DumpPermissionSet",   Help = "Displays a PermissionSet object (debug build only).")]
    [Command(Name = "gchandleleaks",     DefaultOptions = "GCHandleLeaks",       Help = "Helps in tracking down GCHandle leaks.")]
    [Command(Name = "watsonbuckets",     DefaultOptions = "WatsonBuckets",       Help = "Displays the Watson buckets.")]
    public class WindowsSOSCommand : SOSCommandBase
    {
        [FilterInvoke]
        public static bool FilterInvoke(
            [ServiceImport(Optional = true)] ITarget target,
            [ServiceImport(Optional = true)] ManagedOnlyCommandFilter managedOnly,
            [ServiceImport(Optional = true)] IRuntime runtime) =>
                target != null &&
                target.OperatingSystem == OSPlatform.Windows &&
                SOSCommand.FilterInvoke(managedOnly, runtime);
    }

    public class SOSCommandBase : CommandBase
    {
        /// <summary>
        /// Empty service used to prevent native commands from being run
        /// </summary>
        public class ManagedOnlyCommandFilter
        {
        }

        [Argument(Name = "arguments", Help = "Arguments to SOS command.")]
        public string[] Arguments { get; set; }

        [ServiceImport]
        public SOSHost SOSHost { get; set; }

        public override void Invoke()
        {
            Debug.Assert(Arguments != null && Arguments.Length > 0);
            string arguments = string.Concat(Arguments.Skip(1).Select((arg) => arg + " ")).Trim();
            SOSHost.ExecuteCommand(Arguments[0], arguments);
        }

        [HelpInvoke]
        public string GetDetailedHelp()
        {
            return SOSHost.GetHelpText(Arguments[0]);
        }
    }
}
