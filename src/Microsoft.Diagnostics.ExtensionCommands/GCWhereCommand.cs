// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "gcwhere", Help = "Displays the location in the GC heap of the specified address.")]
    public class GCWhereCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Argument(Help = "The address on the GC heap to list near objects")]
        public string Address { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(Address, out ulong address))
            {
                throw new ArgumentException($"Could not parse address: {Address}");
            }

            // We should only ever find zero or one segments, so the output of the table should only have one entry,
            // but if for some reason the dac reports multiple matching segments (probably due to a bug or inconsistent
            // data), then we do want to print all of those out here.

            ClrSegment[] segments = FindSegments(address).OrderBy(seg => seg.SubHeap.Index).ThenBy(seg => seg.Address).ToArray();
            if (segments.Length == 0)
            {
                Console.WriteLine($"Address {address:x} not found in the managed heap.");
                return;
            }

            (int, string) RangeFormat = (segments.Max(seg => RangeSizeForSegment(seg)), "");
            TableOutput output = new(Console, (16, "x"), (4, ""), (16, "x"), (10, ""), RangeFormat, RangeFormat, RangeFormat)
            {
                AlignLeft = true,
            };

            output.WriteRow("Address", "Heap", "Segment", "Generation", "Allocated", "Committed", "Reserved");
            foreach (ClrSegment segment in segments)
            {
                string generation;
                if (segment.ReservedMemory.Contains(address))
                {
                    generation = "reserve";
                }
                else
                {
                    generation = segment.GetGeneration(address) switch
                    {
                        Generation.Generation0 => "0",
                        Generation.Generation1 => "1",
                        Generation.Generation2 => "2",
                        Generation.Frozen => "frozen",
                        Generation.Pinned => "pinned",
                        Generation.Large => "large",
                        _ => "???",
                    };
                }

                object addressColumn = segment.ObjectRange.Contains(address) ? new DmlListNearObj(address) : address;
                output.WriteRow(addressColumn, segment.SubHeap.Index, segment.Address, generation, new DmlDumpHeap(FormatRange(segment.ObjectRange), segment.ObjectRange), FormatRange(segment.CommittedMemory), FormatRange(segment.ReservedMemory));
            }
        }

        private static string FormatRange(MemoryRange range) => $"{range.Start:x}-{range.End:x}";

        private static int RangeSizeForSegment(ClrSegment segment)
        {
            // segment.ObjectRange should always be less length than CommittedMemory
            if (segment.CommittedMemory.Length > segment.ReservedMemory.Length)
            {
                return FormatRange(segment.CommittedMemory).Length;
            }
            else
            {
                return FormatRange(segment.ReservedMemory).Length;
            }
        }

        private IEnumerable<ClrSegment> FindSegments(ulong address)
        {
            // ClrHeap.GetSegmentByAddress doesn't search for reserve memory
            return Runtime.Heap.Segments.Where(seg => seg.ObjectRange.Contains(address) || seg.CommittedMemory.Contains(address) || seg.ReservedMemory.Contains(address));
        }
    }
}
