// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "analyzeoom", Aliases = new[] { "AnalyzeOOM" }, Help = "Displays the info of the last OOM that occurred on an allocation request to the GC heap.")]
    public class AnalyzeOOMCommand : ClrRuntimeCommandBase
    {
        public override void Invoke()
        {
            bool foundOne = false;
            foreach (ClrOutOfMemoryInfo oom in Runtime.Heap.SubHeaps.Select(h => h.OomInfo).Where(oom => oom != null))
            {
                foundOne = true;

                Console.WriteLine(oom.Reason switch
                {
                    OutOfMemoryReason.Budget or OutOfMemoryReason.CantReserve => "OOM was due to an internal .Net error, likely a bug in the GC",
                    OutOfMemoryReason.CantCommit => "Didn't have enough memory to commit",
                    OutOfMemoryReason.LOH => "Didn't have enough memory to allocate an LOH segment",
                    OutOfMemoryReason.LowMem => "Low on memory during GC",
                    OutOfMemoryReason.UnproductiveFullGC => "Could not do a full GC",
                    _ => oom.Reason.ToString() // shouldn't happen, we handle all cases above
                });

                if (oom.GetMemoryFailure != GetMemoryFailureReason.None)
                {
                    string message = oom.GetMemoryFailure switch
                    {
                        GetMemoryFailureReason.ReserveSegment => "Failed to reserve memory",
                        GetMemoryFailureReason.CommitSegmentBegin => "Didn't have enough memory to commit beginning of the segment",
                        GetMemoryFailureReason.CommitEphemeralSegment => "Didn't have enough memory to commit the new ephemeral segment",
                        GetMemoryFailureReason.GrowTable => "Didn't have enough memory to grow the internal GC data structures",
                        GetMemoryFailureReason.CommitTable => "Didn't have enough memory to commit the internal GC data structures",
                        _ => oom.GetMemoryFailure.ToString() // shouldn't happen, we handle all cases above
                    };

                    Console.WriteLine($"Details: {(oom.IsLargeObjectHeap ? "LOH" : "SOH")} {message} {oom.Size:n0} bytes");

                    // If it's a commit error (GetMemoryFailureReason.GrowTable can indicate a reserve
                    // or a commit error since we make one VirtualAlloc call to reserve and commit),
                    // we indicate the available commit space if we recorded it.
                    if (oom.AvailablePageFileMB != 0)
                    {
                        Console.WriteLine($" - on GC entry available commit space was {oom.AvailablePageFileMB:n0} MB");
                    }
                }
            }

            if (!foundOne)
            {
                Console.WriteLine("There was no managed OOM due to allocations on the GC heap");
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"AnalyzeOOM displays the info of the last OOM occurred on an allocation request to
the GC heap (in Server GC it displays OOM, if any, on each GC heap). 

To see the managed exception(s) use the 'clrthreads' command which will show you 
managed exception(s), if any, on each managed thread. If you do see an 
OutOfMemoryException exception you can use the 'printexception' command on it.
To get the full call stack use the ""kb"" command in the debugger for that thread.
For example, to display thread 3's stack use ~3kb.

OOM exceptions could be because of the following reasons:

1) allocation request to GC heap 
   in which case you will see JIT_New* on the call stack because managed code called new.
2) other runtime allocation failure
   for example, failure to expand the finalize queue when GC.ReRegisterForFinalize is
   called.
3) some other code you use throws a managed OOM exception 
   for example, some .NET framework code converts a native OOM exception to managed 
   and throws it.

The 'analyzeoom' command aims to help you with investigating 1) which is the most
difficult because it requires some internal info from GC. The only exception is
we don't support allocating objects larger than 2GB on CLR v2.0 or prior. And this
command will not display any managed OOM because we will throw OOM right away 
instead of even trying to allocate it on the GC heap.

There are 2 legitimate scenarios where GC would return OOM to allocation requests - 
one is if the process is running out of VM space to reserve a segment; the other
is if the system is running out physical memory (+ page file if you have one) so
GC can not commit memory it needs. You can look at these scenarios by using performance
counters or debugger commands. For example for the former scenario the ""!address 
-summary"" debugger command will show you the largest free region in the VM. For
the latter scenario you can look at the ""Memory% Committed Bytes In Use"" see
if you are running low on commit space. One important thing to keep in mind is
when you do this kind of memory analysis it could an aftereffect and doesn't 
completely agree with what this command tells you, in which case the command should
be respected because it truly reflects what happened during GC.

The other cases should be fairly obvious from the call stack.

Sample output:

    {prompt}analyzeoom
    ---------Heap 2 ---------
    Managed OOM occurred after GC #28 (Requested to allocate 1234 bytes)
    Reason: Didn't have enough memory to commit
    Detail: SOH: Didn't have enough memory to grow the internal GC data structures (800000 bytes) - 
            on GC entry available commit space was 500 MB
    ---------Heap 4 ---------
    Managed OOM occurred after GC #12 (Requested to allocate 100000 bytes)
    Reason: Didn't have enough memory to allocate an LOH segment
    Detail: LOH: Failed to reserve memory (16777216 bytes)
";
    }
}
