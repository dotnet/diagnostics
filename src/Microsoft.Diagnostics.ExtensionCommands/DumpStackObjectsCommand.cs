// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpstackobjects", Aliases = new string[] { "dso" }, Help = "Displays all managed objects found within the bounds of the current stack.")]
    public class DumpStackObjectsCommand : CommandBase
    {
        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public IThread CurrentThread { get; set; }

        [ServiceImport]
        public IThreadService ThreadService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [Option(Name = "-verify", Help = "Verify each object and only print ones that are valid objects.")]
        public bool Verify { get; set; }

        [Argument(Name = "StackBounds", Help = "The top and bottom of the stack (in hex).")]
        public string[] Bounds { get; set; }

        public override void Invoke()
        {
            if (Runtime.Heap.Segments.Length == 0)
            {
                throw new DiagnosticsException("Cannot walk heap.");
            }

            MemoryRange range;
            if (Bounds is null || Bounds.Length == 0)
            {
                range = GetStackRange();
            }
            else if (Bounds.Length == 2)
            {
                ulong start = ParseAddress(Bounds[0]) ?? throw new ArgumentException($"Failed to parse start address '{Bounds[0]}'.");
                ulong end = ParseAddress(Bounds[1]) ?? throw new ArgumentException($"Failed to parse end address '{Bounds[1]}'.");
                if (start > end)
                {
                    (start, end) = (end, start);
                }

                range = new(AlignDown(start), AlignUp(end));
            }
            else
            {
                throw new ArgumentException("Invalid arguments.");
            }

            if (range.Start == 0 || range.End == 0)
            {
                throw new ArgumentException($"Invalid range {range.Start:x} - {range.End:x}");
            }

            PrintStackObjects(range);
        }

        private void PrintStackObjects(MemoryRange stack)
        {
            Console.WriteLine($"OS Thread Id: 0x{CurrentThread.ThreadId:x} ({CurrentThread.ThreadIndex})");

            Table output = new(Console, Pointer, DumpObj, TypeName);
            output.WriteHeader("SP/REG", "Object", "Name");

            int regCount = ThreadService.Registers.Count();
            foreach ((ulong address, ClrObject obj) in EnumerateValidObjectsWithinRange(stack).OrderBy(r => r.StackAddress))
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (address < (ulong)regCount)
                {
                    string registerName;
                    if (ThreadService.TryGetRegisterInfo((int)address, out RegisterInfo regInfo))
                    {
                        registerName = regInfo.RegisterName;
                    }
                    else
                    {
                        registerName = $"reg{address}";
                    }

                    output.WriteRow(registerName, obj, obj.Type);
                }
                else
                {
                    output.WriteRow(address, obj, obj.Type);
                }
            }
        }

        /// <summary>
        /// Enumerates all valid objects (and the address they came from) within the given range.
        /// </summary>
        private IEnumerable<(ulong StackAddress, ClrObject Object)> EnumerateValidObjectsWithinRange(MemoryRange range)
        {
            // Note: This implementation is careful to enumerate only real objects and not generate a lot of native
            //       exceptions within the dac.  A naïve implementation could simply read every pointer aligned address
            //       and call ClrHeap.GetObject(objAddr).IsValid.  That approach will generate a lot of exceptions
            //       within the dac trying to validate wild pointers as MethodTables, and it will often find old
            //       pointers which the GC has already swept but not zeroed yet.

            // Sort the list of potential objects so that we can go through each in segment order.
            // Sorting this array saves us a lot of time by not searching for segments.
            IEnumerable<(ulong StackAddress, ulong PotentialObject)> potentialObjects = EnumeratePointersWithinHeapBounds(range);
            potentialObjects = potentialObjects.Concat(EnumerateRegistersWithinHeapBounds());
            potentialObjects = potentialObjects.OrderBy(r => r.PotentialObject);

            ClrSegment currSegment = null;
            List<(ulong StackAddress, ulong PotentialObject)> withinCurrSegment = new(64);
            int segmentIndex = 0;
            foreach ((ulong _, ulong PotentialObject) entry in potentialObjects)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                // Find the segment of the current potential object, or null if it doesn't live
                // within a segment.
                ClrSegment segment = GetSegment(entry.PotentialObject, ref segmentIndex);
                if (segment is null)
                {
                    continue;
                }

                // If we are already processing this segment, just add the entry to the list
                if (currSegment == segment)
                {
                    withinCurrSegment.Add(entry);
                    continue;
                }

                // We are finished walking objects from "currSegment".  If we found any pointers
                // within its range, walk the segment and return every valid object.
                if (withinCurrSegment.Count > 0)
                {
                    foreach ((ulong StackAddress, ClrObject Object) validObject in EnumerateObjectsOnSegment(withinCurrSegment, currSegment))
                    {
                        yield return validObject;
                    }

                    withinCurrSegment.Clear();
                }

                // Update currSegment and add this entry to the processing list.
                currSegment = segment;
                withinCurrSegment.Add(entry);
            }

            // Process leftover items
            if (withinCurrSegment.Count > 0)
            {
                foreach ((ulong StackAddress, ClrObject Object) validObject in EnumerateObjectsOnSegment(withinCurrSegment, currSegment))
                {
                    yield return validObject;
                }
            }
        }

        /// <summary>
        /// Simultaneously walks the withinCurrSegment list and objects on segment returning valid objects found.
        /// </summary>
        private IEnumerable<(ulong StackAddress, ClrObject Object)> EnumerateObjectsOnSegment(List<(ulong StackAddress, ulong PotentialObject)> withinCurrSegment, ClrSegment segment)
        {
            if (withinCurrSegment.Count == 0)
            {
                yield break;
            }

            int index = 0;
            MemoryRange range = new(withinCurrSegment[0].PotentialObject, withinCurrSegment[withinCurrSegment.Count - 1].PotentialObject + 1);
            foreach (ClrObject obj in segment.EnumerateObjects(range, carefully: true))
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (index >= withinCurrSegment.Count)
                {
                    yield break;
                }

                while (index < withinCurrSegment.Count && withinCurrSegment[index].PotentialObject < obj)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    index++;
                }

                while (index < withinCurrSegment.Count && obj == withinCurrSegment[index].PotentialObject)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (Verify)
                    {
                        if (!Runtime.Heap.IsObjectCorrupted(obj, out _))
                        {
                            yield return (withinCurrSegment[index].StackAddress, obj);
                        }
                    }
                    else
                    {
                        yield return (withinCurrSegment[index].StackAddress, obj);
                    }

                    index++;
                }
            }
        }

        private ClrSegment GetSegment(ulong potentialObject, ref int segmentIndex)
        {
            ImmutableArray<ClrSegment> segments = Runtime.Heap.Segments;

            // This function assumes that segmentIndex is always within the bounds of segments
            // and that all objects passed to it are within the given
            // range of segment bounds.
            Debug.Assert(segmentIndex >= 0 && segmentIndex <= segments.Length);
            Debug.Assert(segments[0].ObjectRange.Start <= potentialObject);
            Debug.Assert(potentialObject < segments[segments.Length - 1].ObjectRange.End);

            for (; segmentIndex < segments.Length; segmentIndex++)
            {
                ClrSegment curr = segments[segmentIndex];
                if (potentialObject < curr.Start)
                {
                    return null;
                }
                else if (potentialObject < curr.ObjectRange.End)
                {
                    return segments[segmentIndex];
                }
            }

            // Unreachable.
            Debug.Fail("Reached the end of the segment array.");
            return null;
        }

        private IEnumerable<(ulong RegisterIndex, ulong PotentialObject)> EnumerateRegistersWithinHeapBounds()
        {
            ClrHeap heap = Runtime.Heap;

            // Segments are always sorted by address
            ulong minAddress = heap.Segments[0].ObjectRange.Start;
            ulong maxAddress = heap.Segments[heap.Segments.Length - 1].ObjectRange.End - (uint)MemoryService.PointerSize;

            int regCount = ThreadService.Registers.Count();
            for (int i = 0; i < regCount; i++)
            {
                if (CurrentThread.TryGetRegisterValue(i, out ulong value))
                {
                    if (minAddress <= value && value < maxAddress)
                    {
                        yield return ((ulong)i, value);
                    }
                }
            }
        }

        private IEnumerable<(ulong StackAddress, ulong PotentialObject)> EnumeratePointersWithinHeapBounds(MemoryRange stack)
        {
            Debug.Assert(AlignDown(stack.Start) == stack.Start);
            Debug.Assert(AlignUp(stack.End) == stack.End);

            uint pointerSize = (uint)MemoryService.PointerSize;
            ClrHeap heap = Runtime.Heap;

            // Segments are always sorted by address
            ulong minAddress = heap.Segments[0].ObjectRange.Start;
            ulong maxAddress = heap.Segments[heap.Segments.Length - 1].ObjectRange.End - pointerSize;

            // Read in 64k chunks
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                ulong address = stack.Start;
                while (stack.Contains(address))
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (!MemoryService.ReadMemory(address, buffer, out int read))
                    {
                        break;
                    }

                    read = AlignDown(read);
                    if (read < pointerSize)
                    {
                        break;
                    }

                    for (int i = 0; i < read; i += (int)pointerSize)
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        ulong stackAddress = address + (uint)i;
                        if (!stack.Contains(stackAddress))
                        {
                            yield break;
                        }

                        ulong potentialObj = GetIndex(buffer, i);
                        if (minAddress <= potentialObj && potentialObj < maxAddress)
                        {
                            yield return (stackAddress, potentialObj);
                        }
                    }

                    address += (uint)read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static ulong GetIndex(Span<byte> buffer, int i) => Unsafe.As<byte, nuint>(ref buffer[i]);

        private MemoryRange GetStackRange()
        {
            ulong end = 0;

            int spIndex = ThreadService.StackPointerIndex;
            if (!CurrentThread.TryGetRegisterValue(spIndex, out ulong stackPointer))
            {
                throw new DiagnosticsException($"Unable to get the stack pointer for thread {CurrentThread.ThreadId:x}.");
            }

            // On Windows we have the TEB to know where to end the walk.
            ulong teb = CurrentThread.GetThreadTeb();
            if (teb != 0)
            {
                // The stack base is after the first pointer, see TEB and NT_TIB.
                MemoryService.ReadPointer(teb + (uint)MemoryService.PointerSize, out end);
            }

            if (end == 0)
            {
                end = stackPointer + 0xFFFF;
            }

            return new(AlignDown(stackPointer), AlignUp(end));
        }

        private ulong AlignDown(ulong address)
        {
            ulong mask = ~((ulong)MemoryService.PointerSize - 1);
            return address & mask;
        }

        private int AlignDown(int value)
        {
            int mask = ~(MemoryService.PointerSize - 1);
            return value & mask;
        }

        private ulong AlignUp(ulong address)
        {
            ulong pointerSize = (ulong)MemoryService.PointerSize;
            if (address > ulong.MaxValue - pointerSize)
            {
                return AlignDown(address);
            }

            ulong mask = ~(pointerSize - 1);
            return (address + pointerSize - 1) & mask;
        }
    }
}
