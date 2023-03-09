using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "verifyheap", Help = "Displays a list of all managed objects.")]
    public class VerifyHeapCommand : CommandBase
    {
        private int _totalObjects;

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Option(Name = "--gcheap", Aliases = new string[] { "-h" })]
        public int GCHeap { get; set; } = -1;

        [Argument(Help ="Optional memory ranges in the form of: [Start] [End]")]
        public string[] MemoryRange { get; set; }

        public override void Invoke()
        {
            ClrHeap heap = Runtime.Heap;
            IEnumerable<ClrSegment> segments = heap.Segments;
            if (GCHeap > 0)
            {
                if (!heap.Segments.Any(f => f.SubHeap.Index == GCHeap))
                {
                    Console.WriteLineError($"No gc_heap with index: {GCHeap}");
                    return;
                }

                segments = segments.Where(seg => seg.SubHeap.Index == GCHeap);
            }

            MemoryRange? range = null;
            if (MemoryRange is not null && MemoryRange.Length > 0)
            {
                if (MemoryRange.Length > 2)
                {
                    Console.WriteLineError("Bad arguments.");
                    return;
                }

                if (!ulong.TryParse(MemoryRange[0], NumberStyles.HexNumber, null, out ulong start))
                {
                    Console.WriteLineError($"Invalid start address: {MemoryRange[0]}");
                    return;
                }

                ulong end = segments.Max(seg => seg.End);
                if (MemoryRange.Length == 2)
                {
                    if (!ulong.TryParse(MemoryRange[1], NumberStyles.HexNumber, null, out end))
                    {
                        Console.WriteLineError($"Invalid end address: {MemoryRange[1]}");
                        return;
                    }
                }

                range = new(start, end);
                if (!segments.Any(seg => seg.ObjectRange.Overlaps(range.Value) && (GCHeap < 0 || seg.SubHeap.Index == GCHeap)))
                {
                    if (GCHeap < 0)
                    {
                        Console.WriteLineError($"No GC segments within the range [{start:x}, {end:x}]");
                        return;
                    }
                    else
                    {
                        Console.WriteLineError($"No GC segments within the range [{start:x}, {end:x}] on gc_heap {GCHeap}");
                        return;
                    }
                }
            }


            ClrObject obj = FindMostInterestingObject();
            WriteAndRun(obj, (ushort)0xcccc, () => VerifyHeap(range, GCHeap));

            //VerifyHeap(range, GCHeap);
        }

        #region TEST CODE - Should not be merged into Repo

        private ClrObject FindMostInterestingObject()
        {
            foreach (ClrSegment seg in Runtime.Heap.Segments.OrderByDescending(s => s.ObjectRange.Length))
            {
                foreach (ClrObject obj in seg.EnumerateObjects().OrderByDescending(obj => obj.EnumerateReferenceAddresses().Count()))
                {
                    if (obj.IsFree)
                        continue;

                    if (obj.SyncBlock is null)
                        continue;

                    if (!obj.EnumerateReferenceAddresses().Any())
                        continue;

                    return obj;
                }
            }

            throw new InvalidDataException();
        }

        private unsafe T WriteValue<T>(ulong address, T value)
            where T : unmanaged
        {
            byte[] old = new byte[sizeof(T)];

            Span<T> span = new(&value, 1);
            Span<byte> newBuffer = MemoryMarshal.Cast<T, byte>(span);

            if (!MemoryService.ReadMemory(address, old, old.Length, out int read) || read != old.Length)
                throw new Exception();

            if (!MemoryService.WriteMemory(address, newBuffer, out int written) || written != newBuffer.Length)
                throw new Exception();

            return Unsafe.As<byte, T>(ref old[0]);
        }

        public void WriteAndRun<T>(ulong location,  T value, Action action)
            where T : unmanaged
        {
            T old = WriteValue(location, value);
            try
            {
                action();
            }
            finally
            {
                WriteValue(location, old);
            }
        }

        #endregion

        private void VerifyHeap(MemoryRange? range, int gcheap)
        {
            int errors = 0;
            TableOutput output = null;
            ClrHeap heap = Runtime.Heap;

            // Verify heap
            foreach (var corruption in heap.VerifyHeap(EnumerateObjects(range, gcheap)))
            {
                errors++;
                WriteError(ref output, heap, corruption);
            }

            // Verify SyncBlock table unless the user asked us to verify only a small range:
            int syncBlockErrors = 0;
            if (gcheap < 0 && range is null)
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

                Console.WriteLine();
                Console.WriteLine($"{totalSyncBlocks:n0} SyncBlocks verified, {syncBlockErrors:n0} error{(syncBlockErrors == 1 ? "" :"s")}.");
            }

            Console.WriteLine();
            Console.WriteLine($"{_totalObjects:n0} objects verified, {errors:n0} error{(errors == 1 ? "" : "s")}.");
        }

        private void WriteError(ref TableOutput output, ClrHeap heap, ObjectCorruption corruption)
        {
            ClrObject obj = corruption.Object;

            string message = corruption.Kind switch
            {
                //ObjectCorruptionKind.CouldNotReadMethodTable => $"Could not read method table for Object {objAddress:x}",
                //ObjectCorruptionKind.ObjectNotPointerAligned => $"Object {obj:x} is not pointer aligned",
                //ObjectCorruptionKind.ObjectReferenceNotPointerAligned => $"Object {obj:x} has an unaligned member at {corruption.Offset:x}: is not pointer aligned",
                ObjectCorruptionKind.CouldNotReadObject => $"Could not read object {obj:x} at offset {corruption.Offset:x}: {ReadPointerWithError(obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.ObjectNotOnTheHeap => $"Tried to validate {obj:x} but its address was not on any segment.",
                ObjectCorruptionKind.BadMethodTable => $"Object {obj:x} has an invalid method table {ReadPointerWithError(obj):x}",
                ObjectCorruptionKind.BadObjectReference => $"Object {obj:x} has a bad member at offset {corruption.Offset:x}: {ReadPointerWithError(obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.FreeObjectReference => $"Object {obj:x} contains free object at offset {corruption.Offset:x}: {ReadPointerWithError(obj + (uint)corruption.Offset)}",
                ObjectCorruptionKind.ObjectTooLarge => $"Object {obj:x} is too large, size={obj.Size:x}, segmentEnd: {ValueWithError(heap.GetSegmentByAddress(obj)?.End)}",
                ObjectCorruptionKind.CouldNotReadCardTable => $"Could not verify object {obj:x}: could not read card table",
                ObjectCorruptionKind.CouldNotReadGCDesc => $"Could not verify object {obj:x}: could not read GCDesc",
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
                    output = new(Console, (-4, ""), (-12, "x12"), (-12, "x12"), (22, ""), (0, ""))
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
            columns[i++] = corruption.Object.Address;
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

        private string ValueWithError(int? value, string format = "x", string error = "???")
        {
            if (value.HasValue)
                return value.Value.ToString(format);

            return error;
        }

        private string ValueWithError(ulong? value, string format = "x", string error = "???")
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

        private IEnumerable<ClrObject> EnumerateObjects(MemoryRange? range, int gcheap)
        {
            _totalObjects = 0;

            ClrHeap heap = Runtime.Heap;
            IEnumerable<ClrSegment> segments = heap.Segments;
            if (gcheap >= 0)
                segments = segments.Where(seg => seg.SubHeap.Index == gcheap);

            foreach (ClrSegment segment in segments)
            {
                IEnumerable<ClrObject> objs = /*range.HasValue ? segment.EnumerateObjects(range.Value, carefully: true) :*/ segment.EnumerateObjects(carefully: true);
                foreach (ClrObject obj in objs)
                {
                    _totalObjects++;
                    yield return obj;
                }
            }
        }
    }
}
