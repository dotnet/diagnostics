// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "sizestats", Help = "Size statistics for the GC heap.")]
    public sealed class SizeStatsCommand : ClrRuntimeCommandBase
    {
        public override void Invoke()
        {
            SizeStats(Generation.Generation0, isFree: false);
            SizeStats(Generation.Generation1, isFree: false);
            SizeStats(Generation.Generation2, isFree: false);
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

            SizeStats(Generation.Generation0, isFree: true);
            SizeStats(Generation.Generation1, isFree: true);
            SizeStats(Generation.Generation2, isFree: true);
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
                Console.CancellationToken.ThrowIfCancellationRequested();

                foreach (ClrObject obj in seg.EnumerateObjects())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (!obj.IsValid || obj.IsFree != isFree)
                    {
                        continue;
                    }

                    // If Kind == Ephemeral, we have to further filter by object generation
                    if (seg.Kind == GCSegmentKind.Ephemeral)
                    {
                        if (seg.GetGeneration(obj) != requestedGen)
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

            IEnumerable<(ulong Size, ulong Count)> sorted = from i in stats
                                                            orderby i.Key ascending
                                                            select (i.Key, i.Value);

            ulong cumulativeSize = 0;
            ulong cumulativeCount = 0;
            Table output = null;
            foreach ((ulong size, ulong count) in sorted)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (output is null)
                {
                    output = new(Console, ColumnKind.ByteCount, ColumnKind.Integer, ColumnKind.Integer, ColumnKind.Integer);
                    output.WriteHeader("Size", "Count", "Cumulative Size", "Cumulative Count");
                }

                output.WriteRow(size, count, cumulativeSize, cumulativeCount);

                cumulativeSize += size * count;
                cumulativeCount += count;
            }

            if (output is null)
            {
                Console.WriteLine("(none)");
            }

            Console.WriteLine();
        }

        private static bool FilterByGeneration(ClrSegment seg, Generation gen)
        {
            return seg.Kind switch
            {
                GCSegmentKind.Ephemeral => gen <= Generation.Generation2,
                GCSegmentKind.Generation0 => gen == Generation.Generation0,
                GCSegmentKind.Generation1 => gen == Generation.Generation1,
                GCSegmentKind.Generation2 => gen == Generation.Generation2,
                GCSegmentKind.Frozen => gen == Generation.Frozen,
                GCSegmentKind.Pinned => gen == Generation.Pinned,
                GCSegmentKind.Large => gen == Generation.Large,
                _ => false
            };
        }
    }
}
