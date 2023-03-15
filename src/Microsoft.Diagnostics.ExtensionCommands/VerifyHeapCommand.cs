// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = CommandName, Aliases = new[] { "VerifyHeap" }, Help = "Searches the managed heap for memory corruption..")]
    public class VerifyHeapCommand : ClrRuntimeCommandBase
    {
        private const string CommandName = "verifyheap";

        private int _totalObjects;

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Option(Name = "-heap")]
        public int GCHeap { get; set; } = -1;

        [Option(Name = "-segment")]
        public string Segment { get; set; }

        [Argument(Help ="Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        public override void ExtensionInvoke()
        {
            HeapWithFilters filteredHeap = new(Runtime.Heap);
            if (GCHeap >= 0)
            {
                filteredHeap.GCHeap = GCHeap;
            }

            if (!string.IsNullOrWhiteSpace(Segment))
            {
                filteredHeap.FilterBySegmentHex(Segment);
            }

            if (MemoryRange is not null)
            {
                filteredHeap.FilterByStringMemoryRange(MemoryRange, CommandName);
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
            Table output = null;
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
                {
                    Console.WriteLine();
                }

                Console.WriteLine($"{totalSyncBlocks:n0} SyncBlocks verified, {syncBlockErrors:n0} error{(syncBlockErrors == 1 ? "" :"s")}.");
            }

            if (errors + syncBlockErrors > 0)
            {
                Console.WriteLine();
            }

            Console.WriteLine($"{_totalObjects:n0} objects verified, {errors:n0} error{(errors == 1 ? "" : "s")}.");

            if (errors == 0 && syncBlockErrors == 0)
            {
                Console.WriteLine("No heap corruption detected.");
            }
        }

        private void WriteError(ref Table output, ClrHeap heap, ObjectCorruption corruption)
        {
            string message = GetObjectCorruptionMessage(MemoryService, heap, corruption);
            WriteRow(ref output, heap, corruption, message);
        }

        internal static string GetObjectCorruptionMessage(IMemoryService memory, ClrHeap heap, ObjectCorruption corruption)
        {
            ClrObject obj = corruption.Object;

            string message = corruption.Kind switch
            {
                // odd failures
                ObjectCorruptionKind.ObjectNotOnTheHeap => $"Tried to validate {obj.Address:x} but its address was not on any segment.",
                ObjectCorruptionKind.ObjectNotPointerAligned => $"Object {obj.Address:x} is not pointer aligned",

                // Object failures
                ObjectCorruptionKind.ObjectTooLarge => $"Object {obj.Address:x} is too large, size={obj.Size:x}, segmentEnd: {heap.GetSegmentByAddress(obj)?.End.ToString("x") ?? "???"}",
                ObjectCorruptionKind.InvalidMethodTable => $"Object {obj.Address:x} has an invalid method table {ReadPointerWithError(memory, obj):x}",
                ObjectCorruptionKind.InvalidThinlock => $"Object {obj.Address:x} has an invalid thin lock",
                ObjectCorruptionKind.SyncBlockMismatch => GetSyncBlockFailureMessage(corruption),
                ObjectCorruptionKind.SyncBlockZero => GetSyncBlockFailureMessage(corruption),

                // Object reference failures
                ObjectCorruptionKind.ObjectReferenceNotPointerAligned => $"Object {obj.Address:x} has an unaligned member at offset {corruption.Offset:x}: is not pointer aligned",
                ObjectCorruptionKind.InvalidObjectReference => $"Object {obj.Address:x} has a bad member at offset {corruption.Offset:x}: {ReadPointerWithError(memory, obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.FreeObjectReference => $"Object {obj.Address:x} contains free object at offset {corruption.Offset:x}: {ReadPointerWithError(memory, obj + (uint)corruption.Offset)}",

                // Memory read failures
                ObjectCorruptionKind.CouldNotReadObject => $"Could not read object {obj.Address:x} at offset {corruption.Offset:x}: {ReadPointerWithError(memory, obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.CouldNotReadMethodTable => $"Could not read method table for Object {obj.Address:x}",
                ObjectCorruptionKind.CouldNotReadCardTable => $"Could not verify object {obj.Address:x}: could not read card table",
                ObjectCorruptionKind.CouldNotReadGCDesc => $"Could not verify object {obj.Address:x}: could not read GCDesc",

                _ => ""
            };
            return message;
        }

        private void WriteRow(ref Table output, ClrHeap heap, ObjectCorruption corruption, string message)
        {
            if (output is null)
            {
                if (heap.IsServer)
                {
                    output = new(Console, IntegerWithoutCommas.WithWidth(4), Pointer, ListNearObj, Column.ForEnum<ObjectCorruptionKind>(), Text);
                    output.SetAlignment(Align.Left);

                    output.WriteHeader("Heap", "Segment", "Object", "Failure", "Reason");
                }
                else
                {
                    output = new(Console, Pointer, ListNearObj, Column.ForEnum<ObjectCorruptionKind>(), Text);
                    output.SetAlignment(Align.Left);

                    output.WriteHeader("Segment", "Object", "Failure", "Reason");
                }
            }

            ClrSegment segment = heap.GetSegmentByAddress(corruption.Object);

            object[] columns = new object[output.Columns.Length];
            int i = 0;
            if (heap.IsServer)
            {
                columns[i++] = (object)segment?.SubHeap.Index ?? "???";
            }

            columns[i++] = (object)segment ?? "???";
            columns[i++] = corruption.Object;
            columns[i++] = corruption.Kind;
            columns[i++] = message;

            output.WriteRow(columns);
        }

        private static string GetSyncBlockFailureMessage(ObjectCorruption corruption)
        {
            Debug.Assert(corruption.Kind == ObjectCorruptionKind.SyncBlockZero || corruption.Kind == ObjectCorruptionKind.SyncBlockMismatch);

            // due to how we store syncblock indexes, we can't have a negative index
            // negative index here means the object or CLR didn't have a syncblock
            string result;
            if (corruption.ClrSyncBlockIndex >= 0)
            {
                result = $"Object {corruption.Object:x} should have a SyncBlock index of {corruption.ClrSyncBlockIndex} ";
                if (corruption.SyncBlockIndex >= 0)
                {
                    result += $"but instead had an index of {corruption.SyncBlockIndex}";
                }
                else
                {
                    result += $"but instead had no SyncBlock";
                }
            }
            else
            {
                // We shouldn't have a case where ClrSyncBlockIndex < 0 && SyncBLockIndex < 0, but we'll handle that case anyway
                if (corruption.SyncBlockIndex >= 0)
                {
                    result = $"Object {corruption.Object:x} had a SyncBlock index of {corruption.SyncBlockIndex} but the runtime has no matching SyncBlock";
                }
                else
                {
                    result = $"Object {corruption.Object:x} had no SyncBlock when it was expected to";
                }
            }

            return result;
        }

        private static string ReadPointerWithError(IMemoryService memory, ulong address)
        {
            if (memory.ReadPointer(address, out ulong value))
            {
                return value.ToString("x");
            }

            return "???";
        }
    }
}
