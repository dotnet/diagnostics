// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "ephtoloh", Help = "Finds ephemeral objects which reference the large object heap.")]
    public class FindEphemeralReferencesToLOHCommand : CommandBase
    {
        // IComparer for binary search
        private readonly IComparer<(ClrObject, ClrObject)> _firstObjectComparer = Comparer<(ClrObject, ClrObject)>.Create((x, y) => x.Item1.Address.CompareTo(y.Item1.Address));

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public override void Invoke()
        {
            int segments = Runtime.Heap.Segments.Count(seg => seg.Kind is not GCSegmentKind.Frozen or GCSegmentKind.Pinned);
            if (segments > 50)
            {
                string gcSegKind = Runtime.Heap.SubHeaps[0].HasRegions ? "regions" : "segments";
                Console.WriteLineWarning($"Walking {segments:n0} {gcSegKind}, this may take a moment...");
            }

            TableOutput output = new(Console, (16, "x12"), (64, ""), (16, "x12"));

            // Ephemeral -> Large
            List<(ClrObject From, ClrObject To)> ephToLoh = FindEphemeralToLOH().OrderBy(i => i.From.Address).ThenBy(i => i.To.Address).ToList();
            if (ephToLoh.Count == 0)
            {
                Console.WriteLine("No Ephemeral objects pointing to the Large objects.");
            }
            else
            {
                Console.WriteLine("Ephemeral objects pointing to the Large objects:");
                Console.WriteLine();
                output.WriteRow("Ephemeral", "Ephemeral Type", "Large Object", "Large Object Type");

                foreach ((ClrObject from, ClrObject to) in ephToLoh)
                {
                    output.WriteRow(new DmlDumpObj(from), from.Type?.Name, new DmlDumpObj(to), to.Type?.Name);
                }

                Console.WriteLine();
            }

            // Large -> Ephemeral
            List<(ClrObject From, ClrObject To)> lohToEph = FindLOHToEphemeral().OrderBy(i => i.From.Address).ThenBy(i => i.To.Address).ToList();
            if (lohToEph.Count == 0)
            {
                Console.WriteLine("No Large objects pointing to Ephemeral objects.");
            }
            else
            {
                Console.WriteLine("Large objects pointing to Ephemeral objects:");
                Console.WriteLine();
                output.WriteRow("Ephemeral", "Ephemeral Type", "Large Object", "Large Object Type");

                foreach ((ClrObject from, ClrObject to) in lohToEph)
                {
                    output.WriteRow(new DmlDumpObj(from), from.Type?.Name, new DmlDumpObj(to), to.Type?.Name);
                }

                Console.WriteLine();
            }

            // Ephemeral -> Large -> Ephemeral
            if (ephToLoh.Count != 0 && lohToEph.Count != 0)
            {
                // We'll use output to signify if we need to print a header or not.
                output = null;
                foreach ((ClrObject from, ClrObject to) in ephToLoh)
                {
                    int index = lohToEph.BinarySearch((to, to), _firstObjectComparer);
                    if (index < 0)
                    {
                        continue;
                    }

                    Debug.Assert(to == lohToEph[index].From);
                    ClrObject ephEnd = lohToEph[index].To;

                    if (output is null)
                    {
                        Console.WriteLine($"Ephemeral objects which point to Large objects which point to Ephemeral objects:");
                        Console.WriteLine();
                        output = new(Console, (16, "x12"), (64, ""), (16, "x12"), (64, ""), (16, "x12"));
                        output.WriteRow(new DmlDumpObj(from), from.Type?.Name, new DmlDumpObj(to), to.Type?.Name, new DmlDumpObj(ephEnd), ephEnd.Type?.Name);
                    }
                }

                if (output is null)
                {
                    Console.WriteLine("No Ephemeral objects which point to Large objects which point to Ephemeral objects.");
                }
                else
                {
                    Console.WriteLine();
                }
            }

            foreach ((ClrObject From, ClrObject To) item in ephToLoh)
            {
                if (lohToEph.Any(r => item.To.Address == r.From.Address))
                {
                    Console.WriteLine("error!");
                }
            }
        }

        private IEnumerable<(ClrObject From, ClrObject To)> FindEphemeralToLOH()
        {
            foreach (ClrSegment seg in Runtime.Heap.Segments)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (seg.Kind is GCSegmentKind.Frozen or GCSegmentKind.Large or GCSegmentKind.Generation2 or GCSegmentKind.Pinned)
                {
                    continue;
                }

                foreach (ClrObject obj in seg.EnumerateObjects().Where(obj => obj.IsValid && obj.ContainsPointers))
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    // This handles both regions and segments
                    Generation gen = seg.GetGeneration(obj);
                    if (gen is not Generation.Generation0 or Generation.Generation1)
                    {
                        continue;
                    }

                    foreach (ClrObject objRef in obj.EnumerateReferences(carefully: true, considerDependantHandles: false))
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        if (!objRef.IsValid || objRef.IsFree) // heap corruption
                        {
                            continue;
                        }

                        if (GetGenerationWithoutSegment(objRef) == Generation.Large)
                        {
                            yield return (obj, objRef);
                        }
                    }
                }
            }
        }

        private IEnumerable<(ClrObject From, ClrObject To)> FindLOHToEphemeral()
        {
            foreach (ClrSegment seg in Runtime.Heap.Segments.Where(seg => seg.Kind == GCSegmentKind.Large))
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                foreach (ClrObject obj in seg.EnumerateObjects().Where(obj => obj.IsValid && obj.ContainsPointers))
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    foreach (ClrObject objRef in obj.EnumerateReferences(carefully: true, considerDependantHandles: false))
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        if (!objRef.IsValid || objRef.IsFree) // heap corruption
                        {
                            continue;
                        }

                        if (GetGenerationWithoutSegment(objRef) is Generation.Generation0 or Generation.Generation1)
                        {
                            yield return (obj, objRef);
                        }
                    }
                }
            }
        }

        private Generation GetGenerationWithoutSegment(ClrObject obj)
        {
            ClrSegment seg = Runtime.Heap.GetSegmentByAddress(obj);
            if (seg is not null)
            {
                return seg.GetGeneration(obj);
            }

            return Generation.Unknown;
        }
    }
}
