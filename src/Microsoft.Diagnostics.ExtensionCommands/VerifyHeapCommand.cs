// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "verifyheap", Help = "Searches the managed heap for memory corruption..")]
    public class VerifyHeapCommand : CommandBase
    {
        private int _totalObjects;

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Option(Name = "--gcheap", Aliases = new string[] { "-h" })]
        public int GCHeap { get; set; } = -1;

        [Option(Name = "--segment", Aliases = new string[] { "-s" })]
        public string Segment { get; set; }

        [Argument(Help ="Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        public override void Invoke()
        {
            HeapWithFilters filteredHeap = new(Runtime.Heap);
            if (GCHeap >= 0)
                filteredHeap.GCHeap = GCHeap;

            if (!string.IsNullOrWhiteSpace(Segment))
                filteredHeap.FilterBySegmentHex(Segment);

            if (MemoryRange is not null && MemoryRange.Length > 0)
            {
                if (MemoryRange.Length > 2)
                {
                    string badArgument = MemoryRange.FirstOrDefault(f => f.StartsWith("-") || f.StartsWith("/"));
                    if (badArgument != null)
                        throw new ArgumentException($"Unknown argument: {badArgument}");

                    throw new ArgumentException("Too many arguments to !verifyheap");
                }

                string start = MemoryRange[0];
                string end = MemoryRange.Length > 1 ? MemoryRange[1] : null;
                filteredHeap.FilterByHexMemoryRange(start, end);
            }

            VerifyHeap(filteredHeap.EnumerateFilteredObjects(Console.CancellationToken), verifySyncTable: filteredHeap.HasFilters);
        }

        private IEnumerable<ClrObject> EnumerateWithCount(IEnumerable<ClrObject> objs)
        {
            _totalObjects = 0;
            foreach (ClrObject obj in objs)
            {
                _totalObjects++;
                yield return obj;
            }
        }

        private void VerifyHeap(IEnumerable<ClrObject> objects, bool verifySyncTable)
        {
            // Count _totalObjects
            objects = EnumerateWithCount(objects);

            int errors = 0;
            TableOutput output = null;
            ClrHeap heap = Runtime.Heap;

            // Verify heap
            foreach (ObjectCorruption corruption in heap.VerifyHeap(objects))
            {
                errors++;
                WriteError(ref output, heap, corruption);
            }

            // Verify SyncBlock table unless the user asked us to verify only a small range:
            int syncBlockErrors = 0;
            if (verifySyncTable)
            {
                int totalSyncBlocks = 0;
                foreach (SyncBlock syncBlk in heap.EnumerateSyncBlocks())
                {
                    totalSyncBlocks++;

                    if (syncBlk.Object != 0 && heap.IsObjectCorrupted(syncBlk.Object, out ObjectCorruption corruption))
                    {
                        // If we already printed some errors, create a break in the previous table and
                        // write the table header again
                        if (syncBlockErrors++ == 0)
                        {
                            if (output is not null)
                            {
                                output = null;
                                Console.WriteLine();
                            }

                            Console.WriteLine("SyncBlock Table:");
                        }

                        WriteError(ref output, heap, corruption);
                    }
                }

                if (syncBlockErrors > 0)
                    Console.WriteLine();
                Console.WriteLine($"{totalSyncBlocks:n0} SyncBlocks verified, {syncBlockErrors:n0} error{(syncBlockErrors == 1 ? "" :"s")}.");
            }

            if (errors + syncBlockErrors > 0)
                Console.WriteLine();
            Console.WriteLine($"{_totalObjects:n0} objects verified, {errors:n0} error{(errors == 1 ? "" : "s")}.");
        }

        private void WriteError(ref TableOutput output, ClrHeap heap, ObjectCorruption corruption)
        {
            ClrObject obj = corruption.Object;

            string message = corruption.Kind switch
            {
                ObjectCorruptionKind.CouldNotReadMethodTable => $"Could not read method table for Object {obj.Address:x}",
                ObjectCorruptionKind.ObjectNotPointerAligned => $"Object {obj.Address:x} is not pointer aligned",
                ObjectCorruptionKind.ObjectReferenceNotPointerAligned => $"Object {obj.Address:x} has an unaligned member at {corruption.Offset:x}: is not pointer aligned",
                ObjectCorruptionKind.CouldNotReadObject => $"Could not read object {obj.Address:x} at offset {corruption.Offset:x}: {ReadPointerWithError(obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.ObjectNotOnTheHeap => $"Tried to validate {obj.Address:x} but its address was not on any segment.",
                ObjectCorruptionKind.BadMethodTable => $"Object {obj.Address:x} has an invalid method table {ReadPointerWithError(obj):x}",
                ObjectCorruptionKind.BadObjectReference => $"Object {obj.Address:x} has a bad member at offset {corruption.Offset:x}: {ReadPointerWithError(obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.FreeObjectReference => $"Object {obj.Address:x} contains free object at offset {corruption.Offset:x}: {ReadPointerWithError(obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.ObjectTooLarge => $"Object {obj.Address:x} is too large, size={obj.Size:x}, segmentEnd: {ValueWithError(heap.GetSegmentByAddress(obj)?.End)}",
                ObjectCorruptionKind.CouldNotReadCardTable => $"Could not verify object {obj.Address:x}: could not read card table",
                ObjectCorruptionKind.CouldNotReadGCDesc => $"Could not verify object {obj.Address:x}: could not read GCDesc",
                ObjectCorruptionKind.SyncBlockMismatch => GetSyncBlockFailureMessage(corruption),
                ObjectCorruptionKind.SyncBlockZero => GetSyncBlockFailureMessage(corruption),
                _ => ""
            };

            WriteRow(ref output, heap, corruption, message);
        }

        private void WriteRow(ref TableOutput output, ClrHeap heap, ObjectCorruption corruption, string message)
        {
            if (output is null)
            {
                if (heap.IsServer)
                {
                    output = new(Console, (-4, ""), (-12, "x12"), (-12, "x12"), (32, ""), (0, ""))
                    {
                        AlignLeft = true,
                    };

                    output.WriteRow("Heap", "Segment", "Object", "Failure", "");
                }
                else
                {
                    output = new(Console, (-12, "x12"), (-12, "x12"), (22, ""), (0, ""))
                    {
                        AlignLeft = true,
                    };

                    output.WriteRow("Segment", "Object", "Failure", "");
                }
            }


            ClrSegment segment = heap.GetSegmentByAddress(corruption.Object);

            object[] columns = new object[output.ColumnCount];
            int i = 0;
            if (heap.IsServer)
                columns[i++] = ValueWithError(segment?.SubHeap.Index, format: "", error: "");

            columns[i++] = ValueWithError(segment?.Address, format: "x12", error: "");
            columns[i++] = new DmlExec(corruption.Object.Address, $"!ListNearObj {corruption.Object.Address:x}");
            columns[i++] = corruption.Kind;
            columns[i++] = message;

            output.WriteRow(columns);
        }

        private string GetSyncBlockFailureMessage(ObjectCorruption corruption)
        {
            Debug.Assert(corruption.Kind == ObjectCorruptionKind.SyncBlockZero || corruption.Kind == ObjectCorruptionKind.SyncBlockMismatch);

            // due to how we store syncblock indexes, we can't have a negative index
            // negative index here means the object or CLR didn't have a syncblock
            string result;
            if (corruption.ClrSyncBlockIndex >= 0)
            {
                result = $"Object {corruption.Object:x} should have a SyncBlock index of {corruption.ClrSyncBlockIndex} ";
                if (corruption.SyncBlockIndex >= 0)
                    result += $"but instead had an index of {corruption.SyncBlockIndex}";
                else
                    result += $"but instead had no SyncBlock";
            }
            else
            {
                // We shouldn't have a case where ClrSyncBlockIndex < 0 && SyncBLockIndex < 0, but we'll handle that case anyway
                if (corruption.SyncBlockIndex >= 0)
                    result = $"Object {corruption.Object:x} had a SyncBlock index of {corruption.SyncBlockIndex} but the runtime has no matching SyncBlock";
                else 
                    result = $"Object {corruption.Object:x} had no SyncBlock when it was expected to";
            }

            return result;
        }

        private static string ValueWithError(int? value, string format = "x", string error = "???")
        {
            if (value.HasValue)
                return value.Value.ToString(format);

            return error;
        }

        private static string ValueWithError(ulong? value, string format = "x", string error = "???")
        {
            if (value.HasValue)
                return value.Value.ToString(format);

            return error;
        }

        private string ReadPointerWithError(ulong address)
        {
            if (MemoryService.ReadPointer(address, out ulong value))
                return value.ToString("x");

            return "???";
        }
    }
}
