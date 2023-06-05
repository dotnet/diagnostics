// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "listnearobj", Help = "Displays the object preceding and succeeding the specified address.")]
    public class ListNearObjCommand : CommandBase
    {
        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [Argument(Help = "The address on the GC heap to list near objects")]
        public string Address { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(Address, out ulong objAddress))
            {
                throw new ArgumentException($"Could not parse address: {Address}");
            }

            // Align objAddress
            objAddress &= ~((ulong)MemoryService.PointerSize - 1);

            ClrHeap heap = Runtime.Heap;

            // We'll allow any address within the committed range (but not reserve)
            ClrSegment[] segments = heap.Segments.Where(seg => seg.ObjectRange.Contains(objAddress) || seg.CommittedMemory.Contains(objAddress)).ToArray();

            if (segments.Length == 0)
            {
                // Try again the reserve memory.  We could add this to the query above, but we want to give precedence to
                // allocated/committed memory if we have memory corruption (or an inconsistent GC state) where the reported
                // reserve overlaps with some other segment.  This shouldn't happen in practice.
                heap.Segments.Where(seg => seg.ReservedMemory.Contains(objAddress)).ToArray();
            }

            if (segments.Length == 0)
            {
                Console.WriteLine($"Failed to find the segment of the managed heap where the object {objAddress:x} resides");
                return;
            }

            ClrSegment segment = segments[0];

            if (segments.Length > 1)
            {
                // I've never seen this happen, but better to report it and continue than to error out:
                Console.WriteLine($"Found multiple segments where the object {objAddress:x} resides, this is possibly memory corruption.");
                Console.WriteLine($"Printing values for segment {segment.Address:x}");
            }

            if (segment.ObjectRange.Length == 0)
            {
                Console.WriteLine($"Segment {segment.Address:x} has no objects.");
                return;
            }

            // If we might have allocation contexts in the target memory range, expand the pointer size column
            // so that we can print the allocation context range.
            MemoryRange[] segAllocContexts = heap.EnumerateAllocationContexts().Where(context => segment.ObjectRange.Contains(context.Start)).ToArray();
            int pointerColumnWidth = segAllocContexts.Length > 0 ? Math.Max(segAllocContexts.Max(r => FormatRange(r).Length), 16) : 16;

            Column kindColumn = Text.WithWidth("Expected:".Length).WithAlignment(Align.Left);

            Table output = new(Console, kindColumn, DumpObj.WithWidth(pointerColumnWidth), Text.WithWidth(32), TypeName);

            // Get current object, but objAddress may not point to an object.
            ClrObject curr = heap.GetObject(objAddress);

            bool localConsistency = true;
            bool foundLastObject = false;
            ulong expectedNextObject;

            // Previous object
            ClrObject prev = default;
            if (objAddress > segment.FirstObjectAddress)
            {
                // FindPreviousObjectOnSegment may fail if objAddress is not within ObjectRange
                if (segment.ObjectRange.End <= objAddress)
                {
                    prev = heap.FindPreviousObjectOnSegment(segment.ObjectRange.End - 1, carefully: true);
                }
                else
                {
                    prev = heap.FindPreviousObjectOnSegment(objAddress, carefully: true);
                }

                Debug.Assert(prev < objAddress); // also works if there's no previous object, Address == 0
                localConsistency = VerifyAndPrintObject(output, "Before:", heap, segment, prev) && localConsistency;

                if (prev.IsValid)
                {
                    expectedNextObject = AlignObj(prev + prev.Size, segment);
                }
                else
                {
                    localConsistency = false;
                    expectedNextObject = heap.FindNextObjectOnSegment(prev, carefully: true);
                }

                // Check for an allocation context
                MemoryRange allocContextPlusGap = PrintGapIfExists(output, segment, segAllocContexts, new(prev, expectedNextObject));
                if (allocContextPlusGap.End != 0)
                {
                    Debug.Assert(expectedNextObject < allocContextPlusGap.End);
                    expectedNextObject = allocContextPlusGap.End;
                }

                if (allocContextPlusGap.Contains(objAddress) && curr.IsValid)
                {
                    // The address we were given lives in an allocation context AND it has a valid method table.
                    // It's likely that this is just an inconsistent/transitional state of the heap.  IE the GC
                    // has started to allocate objAddress and we got a stale alloc context.
                    Console.WriteLine($"Address {objAddress:x} has a valid method table inside of an allocation context.");
                }

                // Is prev the end of the segment?
                CheckEndOfSegment(segment, expectedNextObject, prev, ref localConsistency, ref foundLastObject);
            }
            else if (objAddress < segment.FirstObjectAddress)
            {
                Console.WriteLine($"Address {objAddress:x} is before the first object on the segment.");
                expectedNextObject = segment.FirstObjectAddress;
            }
            else
            {
                Console.WriteLine($"Address {objAddress:x} is the first object on the segment.");
                expectedNextObject = segment.FirstObjectAddress;
            }

            if (!foundLastObject)
            {
                // Very odd case that shouldn't happen often:
                if (expectedNextObject < objAddress && segment.ObjectRange.Contains(expectedNextObject))
                {
                    // If we are here, then seg.FindPreviousObjectOnSegment(objAddress) skipped expectedNextObject,
                    // probably due to corruption.
                    localConsistency = false;

                    ClrObject expected = heap.GetObject(expectedNextObject);
                    VerifyAndPrintObject(output, "Expected:", heap, segment, expected);
                    MemoryRange allocContextPlusGap = PrintGapIfExists(output, segment, segAllocContexts, new(expectedNextObject, objAddress));

                    if (allocContextPlusGap.End != 0)
                    {
                        // Whew, we found an allocation context.  We know where to start again:
                        Debug.Assert(expectedNextObject < allocContextPlusGap.End);
                        expectedNextObject = allocContextPlusGap.End;
                    }
                    else
                    {
                        // We don't know where to start next.  If curr is a valid object, use that, if not, try to
                        // move past "expected", if that doesn't work...give up.
                        if (curr.IsValid)
                        {
                            expectedNextObject = curr;
                        }
                        else
                        {
                            ClrObject maybeNextObject = heap.FindNextObjectOnSegment(curr + 1, carefully: true);
                            if (maybeNextObject.IsValid)
                            {
                                expected = maybeNextObject;
                            }
                            else
                            {
                                // Well we can't walk past expected, so this is the end
                                expectedNextObject = segment.ObjectRange.End;
                            }
                        }
                    }

                    // Is expected the end of the segment?
                    CheckEndOfSegment(segment, expectedNextObject, expected, ref localConsistency, ref foundLastObject);
                }
            }

            // No matter what, print curr if it's valid
            bool needToPrintGapForCurrent = false;
            if (curr.IsValid)
            {
                if (segment.ObjectRange.Contains(curr))
                {
                    localConsistency = VerifyAndPrintObject(output, "Current:", heap, segment, curr) && localConsistency;

                    // If curr is valid, we need to print and skip the allocation context
                    expectedNextObject = AlignObj(curr + curr.Size, segment);
                    MemoryRange allocContextPlusGap = PrintGapIfExists(output, segment, segAllocContexts, new(curr, expectedNextObject));
                    if (allocContextPlusGap.End != 0)
                    {
                        Debug.Assert(expectedNextObject <= allocContextPlusGap.End);
                        expectedNextObject = allocContextPlusGap.End;
                    }

                    // Is expected the end of the segment?
                    CheckEndOfSegment(segment, expectedNextObject, curr, ref localConsistency, ref foundLastObject);
                }
                else
                {
                    // If this value lives outside of the object range, it doesn't affect local consistency.  If
                    // we are here, then we are likely looking at a recently collected object on a compacted segment
                    // which hasn't been zeroed yet.
                    Console.WriteLineError($"Object {objAddress:x} is not in the allocated range of the segment, it may have been collected but not zeroed.");
                    VerifyAndPrintObject(output, "Current:", heap, segment, curr);

                    foundLastObject = true;
                }
            }
            else if (expectedNextObject == curr)
            {
                // The objAddress isn't a valid object but it's exactly where one should be
                localConsistency = VerifyAndPrintObject(output, "Current:", heap, segment, curr) && localConsistency;

                // Since curr is invalid, we won't know the size of the object to check for an allocation context.
                // In this case, we'll check again if we found a valid next object to print the gap.
                needToPrintGapForCurrent = true;
            }

            if (!foundLastObject)
            {
                // Determine "Next:" object
                ClrObject next = heap.FindNextObjectOnSegment(objAddress, carefully: true);
                if (next.IsValid)
                {
                    if (needToPrintGapForCurrent)
                    {
                        PrintGapIfExists(output, segment, segAllocContexts, new(curr, next));
                    }

                    localConsistency = VerifyAndPrintObject(output, "Next:", heap, segment, next) && localConsistency;
                    if (next != expectedNextObject)
                    {
                        localConsistency = false;
                        Console.WriteLine($"Expected to find next object at {expectedNextObject:x}, instead found it at {next.Address:x}.");
                    }

                    // Is expected the end of the segment?
                    CheckEndOfSegment(segment, expectedNextObject, curr, ref localConsistency, ref foundLastObject);
                }
                else
                {
                    localConsistency = false;
                    Console.WriteLine($"Could not find object after {objAddress:x}");
                }
            }

            if (localConsistency)
            {
                Console.WriteLine("Heap local consistency confirmed.");
            }
            else
            {
                Console.WriteLine("Heap local consistency not confirmed.");
            }
        }

        private void CheckEndOfSegment(ClrSegment segment, ulong expectedNextObject, ulong prevObjectAddress, ref bool localConsistency, ref bool foundLastObject)
        {
            if (!segment.ObjectRange.Contains(expectedNextObject) && !foundLastObject)
            {
                Console.WriteLineError($"{prevObjectAddress:x} is the last object on the segment");
                if (expectedNextObject != segment.ObjectRange.End)
                {
                    Console.WriteLine($"Error: Expected allocated end at {expectedNextObject:x}, but instead was at {segment.ObjectRange.End:x}");
                    localConsistency = false;
                }

                foundLastObject = true;
            }
        }

        private MemoryRange PrintGapIfExists(Table output, ClrSegment segment, MemoryRange[] segAllocContexts, MemoryRange objectDistance)
        {
            // Print information about allocation context gaps between objects
            MemoryRange range = segAllocContexts.FirstOrDefault(ctx => objectDistance.Overlaps(ctx) || ctx.Contains(objectDistance.End));
            if (range.Start != 0)
            {
                output.Columns[1] = output.Columns[1].WithDml(null);
                output.WriteRow("Gap:", FormatRange(range), FormatSize(range.Length), "GC Allocation Context (expected gap in the heap)");
            }

            // Return the region of memory that does not contain objects.  CLR stores allocation contexts with an ending
            // that's min_object_size away from the next valid object.  We want to display the alloc_context as CLR sees it,
            // but we also need to know the invalid memory range to be sure we don't display a bad error message.
            if (range.End == 0)
            {
                return default;
            }

            uint minObjectSize = (uint)MemoryService.PointerSize * 3;
            return new(range.Start, range.End + AlignObj(minObjectSize, segment));
        }

        private static string FormatRange(MemoryRange range) => $"{range.Start:x}-{range.End:x}";

        private ulong AlignObj(ulong size, ClrSegment seg)
        {
            ulong AlignConst;
            ulong AlignLargeConst = 7;

            if (MemoryService.PointerSize == 4)
            {
                AlignConst = 3;
            }
            else
            {
                AlignConst = 7;
            }

            if (seg.Kind is GCSegmentKind.Large or GCSegmentKind.Pinned)
            {
                return (size + AlignLargeConst) & ~AlignLargeConst;
            }

            return (size + AlignConst) & ~AlignConst;
        }

        private bool VerifyAndPrintObject(Table output, string which, ClrHeap heap, ClrSegment segment, ClrObject obj)
        {
            bool isObjectValid = !heap.IsObjectCorrupted(obj, out ObjectCorruption corruption) && obj.IsValid;

            // ClrObject.Size is not available if IsValid returns false
            string size = FormatSize(obj.IsValid ? obj.Size : 0);
            if (corruption is null)
            {
                output.Columns[1] = output.Columns[1].WithDml(Dml.DumpObj);
                output.WriteRow(which, obj, size, obj.Type);
            }
            else
            {
                output.Columns[1] = output.Columns[1].WithDml(Dml.ListNearObj);
                output.WriteRow(which, obj, size, obj.Type);

                Console.Write($"Error Detected: {VerifyHeapCommand.GetObjectCorruptionMessage(MemoryService, heap, corruption)} ");
                if (Console.SupportsDml)
                {
                    Console.WriteDmlExec("[verify heap]", $"!verifyheap -segment {segment.Address:X}");
                }

                Console.WriteLine();
            }

            return isObjectValid;
        }

        private static string FormatSize(ulong size) => size > 0 ? $"{size:n0} (0x{size:x})" : "";
    }
}
