// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "SizeStats", Help = "Size statistics for the GC heap.")]
    public sealed class SizeStatsCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public override void Invoke()
        {
            SizeStats(Generation.Gen0, isFree: false);
            SizeStats(Generation.Gen1, isFree: false);
            SizeStats(Generation.Gen2, isFree: false);
            SizeStats(Generation.Large, isFree: false);

            bool hasPinned = Runtime.Heap.Segments.Any(seg => seg.Kind == GCSegmentKind.Pinned);
            if (hasPinned)
            {
                SizeStats(Generation.Pinned, isFree: false);
            }

            if (Runtime.Heap.Segments.Any(r => r.Kind == GCSegmentKind.Frozen))
            {
                SizeStats(Generation.Frozen, isFree: false);
            }

            SizeStats(Generation.Gen0, isFree: true);
            SizeStats(Generation.Gen1, isFree: true);
            SizeStats(Generation.Gen2, isFree: true);
            SizeStats(Generation.Large, isFree: true);

            if (hasPinned)
            {
                SizeStats(Generation.Pinned, isFree: true);
            }
        }

        private void SizeStats(Generation requestedGen, bool isFree)
        {
            Dictionary<ulong, ulong> stats = new();
            foreach (ClrSegment seg in Runtime.Heap.Segments.Where(seg => FilterByGeneration(seg, requestedGen)))
            {
                foreach (ClrObject obj in seg.EnumerateObjects())
                {
                    if (!obj.IsValid || obj.IsFree != isFree)
                    {
                        continue;
                    }

                    // If Kind == Ephemeral, we have to further filter by object generation
                    if (seg.Kind == GCSegmentKind.Ephemeral)
                    {
                        if (obj.GetGeneration(seg) != requestedGen)
                        {
                            continue;
                        }
                    }

                    ulong size = (obj.Size + 7u) & ~7u;
                    stats.TryGetValue(size, out ulong count);
                    stats[size] = count + 1;
                }
            }

            string freeStr = isFree ? "free " : "";
            Console.WriteLine($"Size Statistics for {requestedGen.ToString().ToLowerInvariant()} {freeStr}objects");
            Console.WriteLine();

            TableOutput output = new(Console, (16, "n0"), (16, "n0"), (16, "n0"), (16, "n0"));
            output.WriteRow("Size", "Count", "Cumulative Size", "Cumulative Count");


            IEnumerable<(ulong Size, ulong Count)> sorted = from i in stats
                                                            orderby i.Key ascending
                                                            select (i.Key, i.Value);

            ulong cumulativeSize = 0;
            ulong cumulativeCount = 0;
            foreach ((ulong size, ulong count) in sorted)
            {
                cumulativeSize += size * count;
                cumulativeCount += count;
                output.WriteRow(size, count, cumulativeSize, cumulativeCount);
            }

            Console.WriteLine();
        }

        private static bool FilterByGeneration(ClrSegment seg, Generation gen)
        {
            return seg.Kind switch
            {
                GCSegmentKind.Ephemeral => gen <= Generation.Gen2,
                GCSegmentKind.Generation0 => gen == Generation.Gen0,
                GCSegmentKind.Generation1 => gen == Generation.Gen1,
                GCSegmentKind.Generation2 => gen == Generation.Gen2,
                GCSegmentKind.Frozen => gen == Generation.Frozen,
                GCSegmentKind.Pinned => gen == Generation.Pinned,
                GCSegmentKind.Large => gen == Generation.Large,
                _ => false
            };
        }
    }
}
