// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "analyzeoom", Help = "Displays the info of the last OOM that occurred on an allocation request to the GC heap.")]
    public class AnalyzeOOMCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public override void Invoke()
        {
            bool foundOne = false;
            foreach (ClrOutOfMemoryInfo oom in Runtime.Heap.SubHeaps.Select(h => h.OomInfo).Where(oom => oom != null))
            {
                foundOne = true;

                Console.WriteLine(oom.Reason switch
                {
                    OOMReason.Budget or OOMReason.CantReserve => "OOM was due to an internal .Net error, likely a bug in the GC",
                    OOMReason.CantCommit => "Didn't have enough memory to commit",
                    OOMReason.LOH => "Didn't have enough memory to allocate an LOH segment",
                    OOMReason.LowMem => "Low on memory during GC",
                    OOMReason.UnproductiveFullGC => "Could not do a full GC",
                    _ => oom.Reason.ToString() // shouldn't happen, we handle all cases above
                });

                if (oom.GetMemoryFailure != OOMGetMemoryFailure.None)
                {
                    string message = oom.GetMemoryFailure switch
                    {
                        OOMGetMemoryFailure.ReserveSegment => "Failed to reserve memory",
                        OOMGetMemoryFailure.CommitSegmentBegin => "Didn't have enough memory to commit beginning of the segment",
                        OOMGetMemoryFailure.CommitEphemeralSegment => "Didn't have enough memory to commit the new ephemeral segment",
                        OOMGetMemoryFailure.GrowTable => "Didn't have enough memory to grow the internal GC data structures",
                        OOMGetMemoryFailure.CommitTable => "Didn't have enough memory to commit the internal GC data structures",
                        _ => oom.GetMemoryFailure.ToString() // shouldn't happen, we handle all cases above
                    };

                    Console.WriteLine($"Details: {(oom.IsLargeObjectHeap ? "LOH" : "SOH")} {message} {oom.Size:n0} bytes");

                    // If it's a commit error (OOMGetMemoryFailure.GrowTable can indicate a reserve
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
