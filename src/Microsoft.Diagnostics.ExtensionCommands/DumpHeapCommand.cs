// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpheap", Help = "Displays a list of all managed objects.")]
    public class DumpHeapCommand : CommandBase
    {
        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [ServiceImport]
        public DumpHeapService DumpHeap { get; set; }

        [Option(Name = "-mt")]
        public string MethodTableString { get; set; }

        private ulong? MethodTable { get; set; }

        [Option(Name = "-type")]
        public string Type { get; set; }

        [Option(Name = "-stat")]
        public bool StatOnly { get; set; }

        [Option(Name = "-strings")]
        public bool Strings { get; set; }

        [Option(Name = "-short")]
        public bool Short { get; set; }

        [Option(Name = "-min")]
        public ulong Min { get; set; }

        [Option(Name = "-max")]
        public ulong Max { get; set; }

        [Option(Name = "-live")]
        public bool Live { get; set; }

        [Option(Name = "-dead")]
        public bool Dead{ get; set; }

        [Option(Name = "-heap")]
        public int GCHeap { get; set; } = -1;

        [Option(Name = "-segment")]
        public string Segment { get; set; }

        [Option(Name = "-thinlock")]
        public bool ThinLock { get; set; }

        [Argument(Help = "Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        private HeapWithFilters FilteredHeap { get; set; }

        public override void Invoke()
        {
            ParseArguments();

            IEnumerable<ClrObject> objectsToPrint = FilteredHeap.EnumerateFilteredObjects(Console.CancellationToken);

            bool? liveObjectWarning = null;
            if ((Live || Dead) && Short)
            {
                liveObjectWarning = LiveObjects.PrintWarning;
                LiveObjects.PrintWarning = false;
            }

            if (Live)
            {
                objectsToPrint = objectsToPrint.Where(LiveObjects.IsLive);
            }
            else if (Dead)
            {
                objectsToPrint = objectsToPrint.Where(obj => !LiveObjects.IsLive(obj));
            }

            if (Type is not null)
            {
                objectsToPrint = objectsToPrint.Where(obj => obj.Type?.Name?.Contains(Type) ?? false);
            }

            if (MethodTable.HasValue)
            {
                objectsToPrint = objectsToPrint.Where(obj => {
                    ulong mt;
                    if (obj.Type is not null)
                    {
                        mt = obj.Type.MethodTable;
                    }
                    else
                    {
                        MemoryService.ReadPointer(obj, out mt);
                    }

                    return mt == MethodTable.Value;
                });
            }

            if (Min != 0 || Max != 0)
            {
                objectsToPrint = objectsToPrint.Where(obj =>
                {
                    // We cannot get the size of an invalid object
                    if (!obj.IsValid)
                    {
                        return false;
                    }

                    ulong size = obj.Size;
                    if (Min != 0 && size < Min)
                    {
                        return false;
                    }

                    if (Max != 0 && size > Max)
                    {
                        return false;
                    }

                    return true;
                });
            }

            bool printFragmentation = false;
            DumpHeapService.DisplayKind displayKind = DumpHeapService.DisplayKind.Normal;
            if (ThinLock)
            {
                displayKind = DumpHeapService.DisplayKind.ThinLock;
            }
            else if (Strings)
            {
                displayKind = DumpHeapService.DisplayKind.Strings;
            }
            else if (Short)
            {
                displayKind = DumpHeapService.DisplayKind.Short;
            }
            else
            {
                printFragmentation = true;
            }

            DumpHeap.PrintHeap(objectsToPrint, displayKind, StatOnly, printFragmentation);

            if (liveObjectWarning is bool original)
            {
                LiveObjects.PrintWarning = original;
            }
        }

        private void ParseArguments()
        {
            if (Live && Dead)
            {
                Live = false;
                Dead = false;
            }

            if (!string.IsNullOrWhiteSpace(MethodTableString))
            {
                if (TryParseAddress(MethodTableString, out ulong mt))
                {
                    MethodTable = mt;
                }
                else
                {
                    throw new ArgumentException($"Invalid MethodTable: {MethodTableString}");
                }
            }

            FilteredHeap = new(Runtime.Heap);
            if (GCHeap >= 0)
            {
                FilteredHeap.GCHeap = GCHeap;
            }

            if (!string.IsNullOrWhiteSpace(Segment))
            {
                FilteredHeap.FilterBySegmentHex(Segment);
            }

            if (MemoryRange is not null && MemoryRange.Length > 0)
            {
                if (MemoryRange.Length > 2)
                {
                    string badArgument = MemoryRange.FirstOrDefault(f => f.StartsWith("-") || f.StartsWith("/"));
                    if (badArgument != null)
                    {
                        throw new ArgumentException($"Unknown argument: {badArgument}");
                    }

                    throw new ArgumentException("Too many arguments to !dumpheap");
                }

                string start = MemoryRange[0];
                string end = MemoryRange.Length > 1 ? MemoryRange[1] : null;
                FilteredHeap.FilterByHexMemoryRange(start, end);
            }

            if (Min > 0)
            {
                FilteredHeap.MinimumObjectSize = Min;
            }

            if (Max > 0)
            {
                FilteredHeap.MaximumObjectSize = Max;
            }

            if (Strings)
            {
                MethodTable = Runtime.Heap.StringType.MethodTable;
            }

            FilteredHeap.SortSegments = (seg) => seg.OrderBy(seg => seg.Start);
        }
    }
}
