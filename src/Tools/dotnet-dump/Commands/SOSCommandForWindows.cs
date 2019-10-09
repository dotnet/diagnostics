// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Repl;
using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "dumprcw",              AliasExpansion = "DumpRCW",             Help = "Displays information about a Runtime Callable Wrapper.")]
    [Command(Name = "dumpccw",              AliasExpansion = "DumpCCW",             Help = "Displays information about a COM Callable Wrapper.")]
    [Command(Name = "dumppermissionset",    AliasExpansion = "DumpPermissionSet",   Help = "Displays a PermissionSet object (debug build only).")]
    [Command(Name = "traverseheap",         AliasExpansion = "TraverseHeap",        Help = "Writes out a file in a format understood by the CLR Profiler.")]
    [Command(Name = "analyzeoom",           AliasExpansion = "AnalyzeOOM",          Help = "Displays the info of the last OOM occurred on an allocation request to the GC heap.")]
    [Command(Name = "verifyobj",            AliasExpansion = "VerifyObj",           Help = "Checks the object for signs of corruption.")]
    [Command(Name = "listnearobj",          AliasExpansion = "ListNearObj",         Help = "Displays the object preceding and succeeding the address specified.")]
    [Command(Name = "gcheapstat",           AliasExpansion = "GCHeapStat",          Help = "Display various GC heap stats.")]
    [Command(Name = "watsonbuckets",        AliasExpansion = "WatsonBuckets",       Help = "Displays the Watson buckets.")]
    [Command(Name = "threadpool",           AliasExpansion = "ThreadPool",          Help = "Lists basic information about the thread pool.")]
    [Command(Name = "comstate",             AliasExpansion = "COMState",            Help = "Lists the COM apartment model for each thread.")]
    [Command(Name = "gchandles",            AliasExpansion = "GCHandles",           Help = "Provides statistics about GCHandles in the process.")]
    [Command(Name = "objsize",              AliasExpansion = "ObjSize",             Help = "Lists the sizes of the all the objects found on managed threads.")]
    [Command(Name = "gchandleleaks",        AliasExpansion = "GCHandleLeaks",       Help = "Helps in tracking down GCHandle leaks")]
    internal class SOSCommandForWindows : SOSCommand
    {
    }
}
