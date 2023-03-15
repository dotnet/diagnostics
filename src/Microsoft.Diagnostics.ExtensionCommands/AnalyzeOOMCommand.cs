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
        public override void ExtensionInvoke()
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
    }
}
