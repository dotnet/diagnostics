// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [ServiceExport(Scope = ServiceScope.Target)]
    public sealed class NativeAddressHelper
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public IThreadService ThreadService { get; set; }

        [ServiceImport]
        public IRuntimeService RuntimeService { get; set; }

        [ServiceImport]
        public IModuleService ModuleService { get; set; }

        [ServiceImport]
        public IMemoryRegionService MemoryRegionService { get; set; }

        [ServiceImport]
        public IConsoleService Console { get; set; }

        /// <summary>
        /// Enumerates the entire address space, optionally tagging special CLR heaps, and optionally "collapsing"
        /// MEM_RESERVE regions with a heuristic to blame them on the MEM_COMMIT region that came before it.
        /// See <see cref="CollapseReserveRegions"/> for more info.
        /// </summary>
        /// <param name="tagClrMemoryRanges">Whether to "tag" regions with CLR memory for more details.</param>
        /// <param name="includeReserveMemory">Whether to include MEM_RESERVE memory or not in the enumeration.</param>
        /// <param name="tagReserveMemoryHeuristically">Whether to heuristically "blame" MEM_RESERVE regions on what
        /// lives before it in the address space. For example, if there is a MEM_COMMIT region followed by a MEM_RESERVE
        /// region in the address space, this function will "blame" the MEM_RESERVE region on whatever type of memory
        /// the MEM_COMMIT region happens to be.  Usually this will be correct (e.g. the native heap will reserve a
        /// large chunk of memory and commit the beginning of it as it allocates more and more memory...the RESERVE
        /// region was actually "caused" by the Heap space before it).  Sometimes this will simply be wrong when
        /// a MEM_COMMIT region is next to an unrelated MEM_RESERVE region.
        ///
        /// This is a heuristic, so use it accordingly.</param>
        /// <exception cref="InvalidOperationException">If !address fails we will throw InvalidOperationException.  This is usually
        /// because symbols for ntdll couldn't be found.</exception>
        /// <returns>An enumerable of memory ranges.</returns>
        internal IEnumerable<DescribedRegion> EnumerateAddressSpace(bool tagClrMemoryRanges, bool includeReserveMemory, bool tagReserveMemoryHeuristically)
        {
            bool printedTruncatedWarning = false;

            IEnumerable<DescribedRegion> addressResult = from region in MemoryRegionService.EnumerateRegions()
                                where region.State != MemoryRegionState.MEM_FREE
                                select new DescribedRegion(region, ModuleService.GetModuleFromAddress(region.Start));

            if (!includeReserveMemory)
            {
                addressResult = addressResult.Where(m => m.State != MemoryRegionState.MEM_RESERVE);
            }

            List<DescribedRegion> rangeList = addressResult.ToList();
            if (tagClrMemoryRanges)
            {
                foreach (IRuntime runtime in RuntimeService.EnumerateRuntimes())
                {
                    ClrRuntime clrRuntime = runtime.Services.GetService<ClrRuntime>();
                    if (clrRuntime is not null)
                    {
                        foreach (ClrMemoryPointer mem in ClrMemoryPointer.EnumerateClrMemoryAddresses(clrRuntime))
                        {
                            DescribedRegion[] found = rangeList.Where(r => r.Start <= mem.Address && mem.Address < r.End).ToArray();

                            if (found.Length == 0 && mem.Kind != ClrMemoryKind.GCHeapReserve)
                            {
                                Trace.WriteLine($"Warning:  Could not find a memory range for {mem.Address:x} - {mem.Kind}.");

                                if (!printedTruncatedWarning)
                                {
                                    Console.WriteLine($"Warning:  Could not find a memory range for {mem.Address:x} - {mem.Kind}.");
                                    Console.WriteLine($"This crash dump may not be a full dump!");
                                    Console.WriteLine("");

                                    printedTruncatedWarning = true;
                                }

                                // Add the memory range if we know its size.
                                if (mem.Size > 0)
                                {
                                    IModule module = ModuleService.GetModuleFromAddress(mem.Address);
                                    rangeList.Add(new DescribedRegion()
                                    {
                                        Start = mem.Address,
                                        End = mem.Address + mem.Size,
                                        ClrMemoryKind = mem.Kind,
                                        State = mem.Kind == ClrMemoryKind.GCHeapReserve ? MemoryRegionState.MEM_RESERVE : MemoryRegionState.MEM_COMMIT,
                                        Module = module,
                                        Image = module?.FileName,
                                        Protection = MemoryRegionProtection.PAGE_UNKNOWN,
                                        Type = module != null ? MemoryRegionType.MEM_IMAGE : MemoryRegionType.MEM_PRIVATE,
                                        Usage = MemoryRegionUsage.CLR,
                                    });
                                }
                            }
                            else if (found.Length > 1)
                            {
                                Trace.WriteLine($"Warning:  Found multiple memory ranges for entry {mem.Address:x} - {mem.Kind}.");
                            }

                            foreach (DescribedRegion region in found)
                            {
                                if (mem.Kind is ClrMemoryKind.GCHeapReserve or ClrMemoryKind.GCHeapSegment)
                                {
                                    // GC heap segments are special.  We only know a small chunk of memory on the actual allocated
                                    // region.  We want to mark the whole region as GC/GCReserve and not try to divide up chunks for these.
                                    SetRegionKindWithWarning(mem, region);
                                }
                                else if (mem.Size == 0)
                                {
                                    // If we don't know the length of memory, just mark the Region with this tag.
                                    SetRegionKindWithWarning(mem, region);
                                }
                                else
                                {
                                    // If the CLR memory information does contain a length, we'll split up the optionally split up the range into
                                    // multiple entries if this doesn't span the entire segment.
                                    if (region.Start != mem.Address)
                                    {
                                        // If we don't otherwise know what this region is, we'll still blame it on mem.Kind.
                                        // If one contiguous VirtualAlloc call contains a HighFrequencyHeap (for example) then
                                        // it's more correct to say that memory is probably also HighFrequencyHeap than to
                                        // mark it as some other unknown type.  CLR still allocated it, and it's still close
                                        // by the other region kind.
                                        if (region.ClrMemoryKind == ClrMemoryKind.None)
                                        {
                                            region.ClrMemoryKind = mem.Kind;
                                        }

                                        DescribedRegion middleRegion = new(region)
                                        {
                                            Start = mem.Address,
                                            End = mem.Address + mem.Size,
                                            ClrMemoryKind = mem.Kind,
                                            Usage = MemoryRegionUsage.CLR,
                                        };

                                        // we aren't sorted yet, so we don't need to worry about where we insert
                                        rangeList.Add(middleRegion);

                                        if (middleRegion.End < region.End)
                                        {
                                            // The new region doesn't end where the previous region does, so we
                                            // have to create a third region for the end chunk.
                                            DescribedRegion endRegion = new(middleRegion)
                                            {
                                                Start = middleRegion.End,
                                                End = region.End,           // original region end
                                                Usage = region.Usage,
                                                ClrMemoryKind = region.ClrMemoryKind
                                            };

                                            rangeList.Add(endRegion);
                                        }

                                        // Now set the original region to end where the middle chunk begins.
                                        // Region is now the starting region of this set.
                                        region.End = middleRegion.Start;
                                    }
                                    else if (region.Size < mem.Size)
                                    {
                                        SetRegionKindWithWarning(mem, region);

                                        // That's odd.  The memory in the region is smaller than what the CLR thinks this region size should
                                        // be.  We won't go too deep here, only look for regions which start immediately after this one and
                                        // mark it too.  We could go deep here and make this function recursive, continually marking ranges
                                        // if we keep spilling over, but we don't expect this to happen in practice.

                                        bool foundNext = false;
                                        foreach (DescribedRegion next in rangeList.Where(r => r != region && r.Start <= region.End && region.End <= r.End))
                                        {
                                            SetRegionKindWithWarning(mem, next);
                                            foundNext = true;
                                        }

                                        // If we found no matching regions, expand the current region to be the right length.
                                        if (!foundNext)
                                        {
                                            region.End = mem.Address + mem.Size;
                                        }
                                    }
                                    else if (region.Size > mem.Size)
                                    {
                                        // The CLR memory segment is at the beginning of this region.
                                        DescribedRegion newRange = new(region)
                                        {
                                            End = mem.Address + mem.Size,
                                            ClrMemoryKind = mem.Kind
                                        };

                                        region.Start = newRange.End;
                                        if (region.ClrMemoryKind == ClrMemoryKind.None)  // see note above
                                        {
                                            region.ClrMemoryKind = mem.Kind;
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
            }

            DescribedRegion[] ranges = rangeList.OrderBy(r => r.Start).ToArray();

            if (tagReserveMemoryHeuristically)
            {
                foreach (DescribedRegion mem in ranges)
                {
                    string memName = mem.Name;
                    if (memName == "RESERVED")
                    {
                        TagMemoryRecursive(mem, ranges);
                    }
                }
            }

            // On Linux, !address doesn't mark stack space.  Go do that.
            if (Target.OperatingSystem == OSPlatform.Linux)
            {
                MarkStackSpace(ranges);
            }

            return ranges;
        }

        private static void SetRegionKindWithWarning(ClrMemoryPointer mem, DescribedRegion region)
        {
            if (region.ClrMemoryKind != mem.Kind)
            {
                // Only warn when the region kind meaningfully changes.  Many regions are reported as
                // HighFrequencyHeap originally but are classified into more specific regions, so we
                // don't warn for those.
                if (region.ClrMemoryKind is not ClrMemoryKind.None
                    and not ClrMemoryKind.HighFrequencyHeap)
                {
                    Trace.WriteLine($"Warning:  Overwriting range [{region.Start:x},{region.End:x}] {region.ClrMemoryKind} -> [{mem.Address:x},{mem.Address + mem.Size:x}] {mem.Kind}.");
                }

                region.ClrMemoryKind = mem.Kind;
            }

            if (region.Usage == MemoryRegionUsage.Unknown)
            {
                region.Usage = MemoryRegionUsage.CLR;
            }
        }

        private void MarkStackSpace(DescribedRegion[] ranges)
        {
            foreach (IThread thread in ThreadService.EnumerateThreads())
            {
                if (thread.TryGetRegisterValue(ThreadService.StackPointerIndex, out ulong sp) && sp != 0)
                {
                    DescribedRegion range = FindMemory(ranges, sp);
                    if (range is not null)
                    {
                        range.Description = "Stack";
                    }
                }
            }
        }

        private static DescribedRegion FindMemory(DescribedRegion[] ranges, ulong ptr)
        {
            if (ptr < ranges[0].Start || ptr >= ranges.Last().End)
            {
                return null;
            }

            int low = 0;
            int high = ranges.Length - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (ranges[mid].End <= ptr)
                {
                    low = mid + 1;
                }
                else if (ptr < ranges[mid].Start)
                {
                    high = mid - 1;
                }
                else
                {
                    return ranges[mid];
                }
            }

            return null;
        }

        /// <summary>
        /// This method heuristically tries to "blame" MEM_RESERVE regions on what lives before it on the heap.
        /// For example, if there is a MEM_COMMIT region followed by a MEM_RESERVE region in the address space,
        /// this function will "blame" the MEM_RESERVE region on whatever type of memory the MEM_COMMIT region
        /// happens to be.  Usually this will be correct (e.g. the native heap will reserve a large chunk of
        /// memory and commit the beginning of it as it allocates more and more memory...the RESERVE region
        /// was actually "caused" by the Heap space before it).  Sometimes this will simply be wrong when
        /// a MEM_COMMIT region is next to an unrelated MEM_RESERVE region.
        ///
        /// This is a heuristic, so use it accordingly.
        /// </summary>
        internal static void CollapseReserveRegions(DescribedRegion[] ranges)
        {
            foreach (DescribedRegion mem in ranges)
            {
                string memName = mem.Name;
                if (memName == "RESERVED")
                {
                    TagMemoryRecursive(mem, ranges);
                }
            }
        }

        private static DescribedRegion TagMemoryRecursive(DescribedRegion mem, DescribedRegion[] ranges)
        {
            if (mem.Name != "RESERVED")
            {
                return mem;
            }

            DescribedRegion found = ranges.SingleOrDefault(r => r.End == mem.Start);
            if (found is null)
            {
                return null;
            }

            DescribedRegion nonReserved = TagMemoryRecursive(found, ranges);
            if (nonReserved is null)
            {
                return null;
            }

            mem.Description = nonReserved.Name;
            return nonReserved;
        }

        internal IEnumerable<(ulong Address, ulong Pointer, DescribedRegion MemoryRange)> EnumerateRegionPointers(ulong start, ulong end, DescribedRegion[] ranges)
        {
            ulong[] array = ArrayPool<ulong>.Shared.Rent(4096);
            int arrayBytes = array.Length * sizeof(ulong);
            try
            {
                ulong curr = start;
                ulong remaining = end - start;

                while (remaining > 0)
                {
                    int size = Math.Min(remaining > int.MaxValue ? int.MaxValue : (int)remaining, arrayBytes);
                    bool res = ReadMemory(curr, array, size, out int bytesRead);
                    if (!res || bytesRead <= 0)
                    {
                        break;
                    }

                    for (int i = 0; i < bytesRead / sizeof(ulong); i++)
                    {
                        ulong ptr = array[i];

                        DescribedRegion found = FindMemory(ranges, ptr);
                        if (found is not null)
                        {
                            yield return (curr + (uint)i * sizeof(ulong), ptr, found);
                        }
                    }

                    curr += (uint)bytesRead;
                    remaining -= (uint)bytesRead; ;
                }

            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(array);
            }
        }

        private unsafe bool ReadMemory(ulong start, ulong[] array, int size, out int bytesRead)
        {
            fixed (ulong* ptr = array)
            {
                Span<byte> buffer = new(ptr, size);
                MemoryService.ReadMemory(start, buffer, out bytesRead);
                return bytesRead == size;
            }
        }

        internal sealed class DescribedRegion : IMemoryRegion
        {
            public DescribedRegion()
            {
            }

            public DescribedRegion(IMemoryRegion region, IModule module)
            {
                Module = module;
                Start = region.Start;
                End = region.End;
                Type = region.Type;
                State = region.State;
                Protection = region.Protection;
                Usage = region.Usage;
                Image = region.Image;
            }

            public DescribedRegion(DescribedRegion copyFrom)
            {
                Module = copyFrom.Module;
                Start = copyFrom.Start;
                End = copyFrom.End;
                Type = copyFrom.Type;
                State = copyFrom.State;
                Protection = copyFrom.Protection;
                Usage = copyFrom.Usage;
                Image = copyFrom.Image;
                Description = copyFrom.Description;
                ClrMemoryKind = copyFrom.ClrMemoryKind;
            }

            public IModule Module { get; internal set; }

            public ulong Start { get; internal set; }

            public ulong End { get; internal set; }

            public MemoryRegionType Type { get; internal set; }

            public MemoryRegionState State { get; internal set; }

            public MemoryRegionProtection Protection { get; internal set; }

            public MemoryRegionUsage Usage { get; internal set; }

            public string Image { get; internal set; }

            public string Description { get; internal set; }

            public ClrMemoryKind ClrMemoryKind { get; internal set; }

            public ulong Size => End <= Start ? 0 : End - Start;

            public string Name
            {
                get {
                    if (ClrMemoryKind != ClrMemoryKind.None)
                    {
                        if (ClrMemoryKind == ClrMemoryKind.GCHeapReserve)
                        {
                            return $"[{ClrMemoryKind}]";
                        }

                        return ClrMemoryKind.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(Description))
                    {
                        return Description;
                    }

                    if (State == MemoryRegionState.MEM_RESERVE)
                    {
                        return "[RESERVED]";
                    }
                    else if (State == MemoryRegionState.MEM_FREE)
                    {
                        return "[FREE]";
                    }

                    if (Type == MemoryRegionType.MEM_IMAGE || !string.IsNullOrWhiteSpace(Image))
                    {
                        return "Image";
                    }

                    string result = Protection.ToString();
                    if (Type == MemoryRegionType.MEM_MAPPED)
                    {
                        if (string.IsNullOrWhiteSpace(result))
                        {
                            result = Type.ToString();
                        }
                        else
                        {
                            result = result.Replace("PAGE", "MAPPED");
                        }
                    }

                    return result;
                }
            }
        }
    }
}
