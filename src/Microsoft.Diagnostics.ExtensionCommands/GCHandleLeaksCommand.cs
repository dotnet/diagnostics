// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.StressLogs;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "gchandleleaks", Help = "Reports strong and pinned GCHandles that could not be found in a scan of process memory.")]
    public sealed class GCHandleLeaksCommand : CommandBase
    {
        private const int BufferSize = 64 * 1024;

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport(Optional = true)]
        public NativeAddressHelper AddressHelper { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [FilterInvoke(Message = "The memory region service does not exist. This command is only supported under windbg/cdb debuggers.")]
        public static bool FilterInvoke([ServiceImport(Optional = true)] ClrRuntime runtime, [ServiceImport(Optional = true)] NativeAddressHelper helper)
            => runtime != null && helper != null;

        public override void Invoke()
        {
            Console.WriteLine("-------------------------------------------------------------------------------");
            Console.WriteLine("GCHandleLeaks will report any GCHandles that couldn't be found in memory.      ");
            Console.WriteLine("Strong and Pinned GCHandles are reported at this time. You can safely abort the");
            Console.WriteLine("memory scan with Control-C or Control-Break.                                   ");
            Console.WriteLine("-------------------------------------------------------------------------------");

            // Collect the pinned and strong handle slot addresses (low bit masked,
            // matching the native FindAllPinnedAndStrong filter).
            List<ulong> handles = [];
            HashSet<ulong> handleSet = [];
            foreach (ClrHandle handle in Runtime.EnumerateHandles())
            {
                if (handle.IsStrong || handle.HandleKind is ClrHandleKind.Pinned or ClrHandleKind.AsyncPinned)
                {
                    ulong address = handle.Address & ~1ul;
                    if (handleSet.Add(address))
                    {
                        handles.Add(address);
                    }
                }
            }

            Console.WriteLine($"Found {handles.Count} handles:");
            for (int i = 0; i < handles.Count; i++)
            {
                Console.Write($"{handles[i]:x}\t");
                if ((i + 1) % 4 == 0)
                {
                    Console.WriteLine();
                }
            }

            Console.WriteLine();
            Console.WriteLine("Searching memory");

            MemoryRange[] stressRanges = (Runtime.StressLog?.EnumerateMemoryRanges() ?? Enumerable.Empty<MemoryRange>()).ToArray();
            Console.WriteLine(stressRanges.Length > 0
                ? "Reference found in stress log will be ignored"
                : "Failed to read whole or part of stress log, some references may come from stress log");

            HashSet<ulong> found = [];
            int pointerSize = MemoryService.PointerSize;
            byte[] buffer = new byte[BufferSize];
            bool aborted = false;
            // No strong/pinned handles means there is nothing to find in memory, so skip the
            // (potentially very slow) full address-space scan entirely.
            bool allFound = handleSet.Count == 0;

            foreach (IMemoryRegion region in AddressHelper.MemoryRegionService.EnumerateRegions())
            {
                if (aborted || allFound)
                {
                    break;
                }

                if (region.State != MemoryRegionState.MEM_COMMIT)
                {
                    continue;
                }

                ulong regionStart = region.Start;
                ulong regionSize = region.Size;
                for (ulong offset = 0; offset < regionSize;)
                {
                    if (Console.CancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"Quitting at {regionStart + offset:x} due to user abort");
                        aborted = true;
                        break;
                    }

                    int toRead = (int)Math.Min((ulong)buffer.Length, regionSize - offset);
                    toRead -= toRead % pointerSize;
                    if (toRead == 0)
                    {
                        break;
                    }

                    if (!MemoryService.ReadMemory(regionStart + offset, buffer.AsSpan(0, toRead), out int bytesRead) || bytesRead < pointerSize)
                    {
                        offset += (ulong)toRead;
                        continue;
                    }

                    bytesRead -= bytesRead % pointerSize;
                    for (int i = 0; i + pointerSize <= bytesRead; i += pointerSize)
                    {
                        ulong value = pointerSize == 8
                            ? BitConverter.ToUInt64(buffer, i)
                            : BitConverter.ToUInt32(buffer, i);
                        value &= ~1ul;

                        if (handleSet.Contains(value))
                        {
                            ulong location = regionStart + offset + (ulong)i;
                            if (InStressLog(stressRanges, location))
                            {
                                Console.WriteLine($"Found {value:x} in stress log at location {location:x}, reference not counted");
                            }
                            else
                            {
                                found.Add(value);
                                Console.WriteLine($"Found {value:x} at location {location:x}");

                                // Every handle has been located in memory; no need to keep scanning.
                                if (found.Count == handleSet.Count)
                                {
                                    allFound = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (allFound)
                    {
                        break;
                    }

                    offset += (ulong)bytesRead;
                }
            }

            List<ulong> notFound = handles.Where(h => !found.Contains(h)).ToList();
            if (notFound.Count > 0)
            {
                Console.WriteLine("------------------------------------------------------------------------------");
                Console.WriteLine("Some handles were not found. If the number of not-found handles grows over the");
                Console.WriteLine("lifetime of your application, you may have a GCHandle leak. This will cause   ");
                Console.WriteLine("the GC Heap to grow larger as objects are being kept alive, referenced only   ");
                Console.WriteLine("by the orphaned handle. If the number doesn't grow over time, note that there ");
                Console.WriteLine("may be some noise in this output, as an unmanaged application may be storing  ");
                Console.WriteLine("the handle in a non-standard way, perhaps with some bits flipped. The memory  ");
                Console.WriteLine("scan wouldn't be able to find those.                                          ");
                Console.WriteLine("------------------------------------------------------------------------------");

                Console.WriteLine($"Didn't find {notFound.Count} handles:");
                for (int i = 0; i < notFound.Count; i++)
                {
                    Console.Write($"{notFound[i]:x}\t");
                    if ((i + 1) % 4 == 0)
                    {
                        Console.WriteLine();
                    }
                }

                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("------------------------------------------------------------------------------");
                Console.WriteLine(aborted ? "All handles found even though you aborted." : "All handles found.");
                Console.WriteLine("A leak may still exist because in a general scan of process memory SOS can't  ");
                Console.WriteLine("differentiate between garbage and valid structures, so you may have false     ");
                Console.WriteLine("positives. If you still suspect a leak, use this function over time to        ");
                Console.WriteLine("identify a possible trend.                                                    ");
                Console.WriteLine("------------------------------------------------------------------------------");
            }
        }

        private static bool InStressLog(MemoryRange[] ranges, ulong address)
        {
            foreach (MemoryRange range in ranges)
            {
                if (range.Contains(address))
                {
                    return true;
                }
            }

            return false;
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
GCHandleLeaks

Reports any strong or pinned GCHandles that could not be found in a scan of
process memory. The command enumerates the pinned and strong handles, then scans
all committed memory for references to those handle slots. Handle references that
fall inside the stress log are ignored. Handles with no reference found may
indicate a GCHandle leak. The memory scan can be aborted with Control-C.
";
    }
}
