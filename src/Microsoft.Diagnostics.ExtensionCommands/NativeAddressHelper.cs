using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using System.Buffers;

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
            var addressResult = from region in MemoryRegionService.EnumerateRegions()
                                where region.State != MemoryRegionState.MEM_FREE
                                select new DescribedRegion(region, ModuleService.GetModuleFromAddress(region.Start));

            if (!includeReserveMemory)
                addressResult = addressResult.Where(m => m.State != MemoryRegionState.MEM_RESERVE);

            DescribedRegion[] ranges = addressResult.OrderBy(r => r.Start).ToArray();
            if (tagClrMemoryRanges)
            {
                foreach (IRuntime runtime in RuntimeService.EnumerateRuntimes())
                {
                    ClrRuntime clrRuntime = runtime.Services.GetService<ClrRuntime>();
                    if (clrRuntime is not null)
                    {
                        foreach (ClrMemoryPointer mem in ClrMemoryPointer.EnumerateClrMemoryAddresses(clrRuntime))
                        {
                            var found = ranges.Where(m => m.Start <= mem.Address && mem.Address < m.End).ToArray();

                            if (found.Length == 0)
                                Trace.WriteLine($"Warning:  Could not find a memory range for {mem.Address:x} - {mem.Kind}.");
                            else if (found.Length > 1)
                                Trace.WriteLine($"Warning:  Found multiple memory ranges for entry {mem.Address:x} - {mem.Kind}.");

                            foreach (var entry in found)
                            {
                                if (entry.ClrMemoryKind != ClrMemoryKind.None && entry.ClrMemoryKind != mem.Kind)
                                    Trace.WriteLine($"Warning:  Overwriting range {entry.Start:x} {entry.ClrMemoryKind} -> {mem.Kind}.");

                                entry.ClrMemoryKind = mem.Kind;
                            }
                        }
                    }
                }
            }

            if (tagReserveMemoryHeuristically)
            {
                foreach (DescribedRegion mem in ranges)
                {
                    string memName = mem.Name;
                    if (memName == "RESERVED")
                        TagMemoryRecursive(mem, ranges);
                }
            }

            // On Linux, !address doesn't mark stack space.  Go do that.
            if (Target.OperatingSystem == OSPlatform.Linux)
                MarkStackSpace(ranges);

            return ranges;
        }

        private void MarkStackSpace(DescribedRegion[] ranges)
        {
            foreach (IThread thread in ThreadService.EnumerateThreads())
            {
                if (thread.TryGetRegisterValue(ThreadService.StackPointerIndex, out ulong sp) && sp != 0)
                {
                    DescribedRegion range = FindMemory(ranges, sp);
                    if (range is not null)
                        range.Description = "Stack";
                }
            }
        }

        private static DescribedRegion FindMemory(DescribedRegion[] ranges, ulong ptr)
        {
            if (ptr < ranges[0].Start || ptr >= ranges.Last().End)
                return null;

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
                    TagMemoryRecursive(mem, ranges);
            }
        }

        private static DescribedRegion TagMemoryRecursive(DescribedRegion mem, DescribedRegion[] ranges)
        {
            if (mem.Name != "RESERVED")
                return mem;

            DescribedRegion found = ranges.SingleOrDefault(r => r.End == mem.Start);
            if (found is null)
                return null;

            DescribedRegion nonReserved = TagMemoryRecursive(found, ranges);
            if (nonReserved is null)
                return null;

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
                        break;

                    for (int i = 0; i < bytesRead / sizeof(ulong); i++)
                    {
                        ulong ptr = array[i];

                        DescribedRegion found = FindMemory(ranges, ptr);
                        if (found is not null)
                            yield return (curr + (uint)i * sizeof(ulong), ptr, found);
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

        internal class DescribedRegion : IMemoryRegion
        {
            private readonly IMemoryRegion _region;

            public IModule Module { get; }

            public DescribedRegion(IMemoryRegion region, IModule module)
            {
                _region = region;
                Module = module;
            }

            public ulong Start => _region.Start;

            public ulong End => _region.End;

            public ulong Size => _region.Size;

            public MemoryRegionType Type => _region.Type;

            public MemoryRegionState State => _region.State;

            public MemoryRegionProtection Protection => _region.Protection;

            public MemoryRegionUsage Usage => _region.Usage;

            public string Image => _region.Image;

            public string Description { get; internal set; }

            public ClrMemoryKind ClrMemoryKind { get; internal set; }
            public ulong Length => End <= Start ? 0 : End - Start;

            public string Name
            {
                get
                {
                    if (ClrMemoryKind != ClrMemoryKind.None)
                        return ClrMemoryKind.ToString();

                    if (!string.IsNullOrWhiteSpace(Description))
                        return Description;

                    if (State == MemoryRegionState.MEM_RESERVE)
                        return "RESERVED";
                    else if (State == MemoryRegionState.MEM_FREE)
                        return "FREE";

                    if (Type == MemoryRegionType.MEM_IMAGE || !string.IsNullOrWhiteSpace(Image))
                        return "IMAGE";

                    string result = Protection.ToString();
                    if (Type == MemoryRegionType.MEM_MAPPED)
                    {
                        if (string.IsNullOrWhiteSpace(result))
                            result = Type.ToString();
                        else
                            result = result.Replace("PAGE", "MAPPED");
                    }

                    return result;
                }
            }
        }
    }
}
