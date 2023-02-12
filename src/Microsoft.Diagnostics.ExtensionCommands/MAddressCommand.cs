using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using System.Buffers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "maddress", Help = "Displays a breakdown of the virtual address space.")]
    public class MAddressCommand : ExtensionCommandBase
    {
        [Option(Name = "--list", Aliases = new string[] { "-l", "--all", }, Help = "Prints the full list of annotated memory regions.")]
        public bool ListAll { get; set; }

        [Option(Name = "--images", Aliases = new string[] { "-i", "-image", "-img", "--img" }, Help = "Prints the table of image memory usage.")]
        public bool ShowImageTable { get; set; }

        [Option(Name = "--includeReserve", Help ="Include MEM_RESERVE regions in the output.")]
        public bool IncludeReserveMemory { get; set; }

        [Option(Name = "--tagReserve", Help = "Heuristically tag MEM_RESERVE regions based on adjacent memory regions.")]
        public bool TagReserveMemoryHeuristically { get; set; }

        [ServiceImport]
        public IMemoryRegionService MemoryRegionService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        private IDataReader DataReader => Runtime.DataTarget.DataReader;

        public override void ExtensionInvoke()
        {
            if (TagReserveMemoryHeuristically && !IncludeReserveMemory)
                throw new DiagnosticsException("Cannot use --tagReserve without --includeReserve");

            PrintMemorySummary(ListAll, ShowImageTable, IncludeReserveMemory, TagReserveMemoryHeuristically);
        }

        public void PrintMemorySummary(bool printAllMemory, bool showImageTable, bool includeReserveMemory, bool tagReserveMemoryHeuristically)
        {
            IEnumerable<DescribedRegion> memoryRanges = EnumerateAddressSpace(tagClrMemoryRanges: true, includeReserveMemory, tagReserveMemoryHeuristically);
            if (!includeReserveMemory)
                memoryRanges = memoryRanges.Where(m => m.State != MemoryRegionState.MEM_RESERVE);

            DescribedRegion[] ranges = memoryRanges.ToArray();

            int nameSizeMax = ranges.Max(r => r.Name.Length);

            // Tag reserved memory based on what's adjacent.
            if (tagReserveMemoryHeuristically)
                CollapseReserveRegions(ranges);

            if (printAllMemory)
            {
                int kindSize = ranges.Max(r => r.Type.ToString().Length);
                int stateSize = ranges.Max(r => r.State.ToString().Length);
                int protectSize = ranges.Max(r => r.Protection.ToString().Length);

                TableOutput output = new(Console, (nameSizeMax, ""), (12, "x"), (12, "x"), (12, ""), (kindSize, ""), (stateSize, ""), (protectSize, ""))
                {
                    AlignLeft = true,
                    Divider = " | "
                };

                output.WriteRowWithSpacing('-', "Memory Kind", "StartAddr", "EndAddr-1", "Size", "Type", "State", "Protect", "Image");
                foreach (DescribedRegion mem in ranges)
                    output.WriteRow(mem.Name, mem.Start, mem.End, mem.Length.ConvertToHumanReadable(), mem.Type, mem.State, mem.Protection, mem.Image);

                output.WriteSpacer('-');
            }

            if (showImageTable)
            {
                var imageGroups = from mem in ranges.Where(r => r.State != MemoryRegionState.MEM_RESERVE && r.Image != null)
                                  group mem by mem.Image into g
                                  let Size = g.Sum(k => (long)(k.End - k.Start))
                                  orderby Size descending
                                  select new
                                  {
                                      Image = g.Key,
                                      Count = g.Count(),
                                      Size
                                  };

                int moduleLen = Math.Max(80, ranges.Max(r => r.Image?.Length ?? 0));

                TableOutput output = new(Console, (moduleLen, ""), (8, "n0"), (12, ""), (24, "n0"))
                {
                    Divider = " | "
                };

                output.WriteRowWithSpacing('-', "Image", "Regions", "Size", "Size (bytes)");

                int count = 0;
                long size = 0;
                foreach (var item in imageGroups)
                {
                    output.WriteRow(item.Image, item.Count, item.Size.ConvertToHumanReadable(), item.Size);
                    count += item.Count;
                    size += item.Size;
                }

                output.WriteSpacer('-');
                output.WriteRow("[TOTAL]", count, size.ConvertToHumanReadable(), size);
                WriteLine("");
            }


            // Print summary table unconditionally
            {
                var grouped = from mem in ranges
                              let name = mem.Name
                              group mem by name into g
                              let Count = g.Count()
                              let Size = g.Sum(f => (long)(f.End - f.Start))
                              orderby Size descending
                              select new
                              {
                                  Name = g.Key,
                                  Count,
                                  Size
                              };

                TableOutput output = new(Console, (-nameSizeMax, ""), (8, "n0"), (12, ""), (24, "n0"))
                {
                    Divider = " | "
                };

                output.WriteRowWithSpacing('-', "Region Type", "Count", "Size", "Size (bytes)");

                int count = 0;
                long size = 0;
                foreach (var item in grouped)
                {
                    output.WriteRow(item.Name, item.Count, item.Size.ConvertToHumanReadable(), item.Size);
                    count += item.Count;
                    size += item.Size;
                }

                output.WriteSpacer('-');
                output.WriteRow("[TOTAL]", count, size.ConvertToHumanReadable(), size);
            }
        }

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
                                select new DescribedRegion(region);

            if (!includeReserveMemory)
                addressResult = addressResult.Where(m => m.State != MemoryRegionState.MEM_RESERVE);

            DescribedRegion[] ranges = addressResult.OrderBy(r => r.Start).ToArray();
            if (tagClrMemoryRanges)
            {
                foreach (ClrMemoryPointer mem in ClrMemoryPointer.EnumerateClrMemoryAddresses(Runtime))
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
            if (DataReader.TargetPlatform == OSPlatform.Linux)
                MarkStackSpace(ranges);

            return ranges;
        }

        private void MarkStackSpace(DescribedRegion[] ranges)
        {
            IThreadReader threadReader = DataReader as IThreadReader;
            Architecture arch = DataReader.Architecture;
            int size = arch switch
            {
                Architecture.Arm => ArmContext.Size,
                Architecture.Arm64 => Arm64Context.Size,
                Architecture.X86 => X86Context.Size,
                Architecture.X64 => AMD64Context.Size,
                _ => 0
            };

            if (size > 0 && threadReader is not null)
            {
                byte[] rawBuffer = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    Span<byte> buffer = rawBuffer.AsSpan().Slice(0, size);

                    foreach (uint thread in threadReader.EnumerateOSThreadIds())
                    {
                        ulong sp = GetStackPointer(arch, buffer, thread);

                        DescribedRegion range = FindMemory(ranges, sp);
                        if (range is not null)
                            range.Description = "Stack";
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rawBuffer);
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

        private unsafe ulong GetStackPointer(Architecture arch, Span<byte> buffer, uint thread)
        {
            ulong sp = 0;

            bool res = DataReader.GetThreadContext(thread, 0, buffer);
            if (res)
            {
                switch (arch)
                {
                    case Architecture.X86:
                        fixed (byte* ptrCtx = buffer)
                        {
                            X86Context* ctx = (X86Context*)ptrCtx;
                            sp = ctx->Esp;
                        }
                        break;

                    case Architecture.X64:
                        fixed (byte* ptrCtx = buffer)
                        {
                            AMD64Context* ctx = (AMD64Context*)ptrCtx;
                            sp = ctx->Rsp;
                        }
                        break;

                    case Architecture.Arm64:
                        fixed (byte* ptrCtx = buffer)
                        {
                            Arm64Context* ctx = (Arm64Context*)ptrCtx;
                            sp = ctx->Sp;
                        }
                        break;

                    case Architecture.Arm:
                        fixed (byte* ptrCtx = buffer)
                        {
                            ArmContext* ctx = (ArmContext*)ptrCtx;
                            sp = ctx->Sp;
                        }
                        break;
                }
            }

            return sp;
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
        private static void CollapseReserveRegions(DescribedRegion[] ranges)
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
                bytesRead = DataReader.Read(start, buffer);
                return bytesRead == size;
            }
        }

        internal class DescribedRegion : IMemoryRegion
        {
            private readonly IMemoryRegion _region;

            public DescribedRegion(IMemoryRegion region)
            {
                _region = region;
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

        protected override string GetDetailedHelp()
        {
            return
@"-------------------------------------------------------------------------------
!maddress is a managed version of !address, which attempts to annotate all memory
with information about CLR's heaps.

usage: !maddress [--list] [--images] [--includeReserve [--tagReserve]]

Flags:
    --list
        Shows the full list of annotated memory regions and not just the statistics
        table.

    --images
        Summarizes the memory ranges consumed by images in the process.
        
    --includeReserve
        Include reserved memory (MEM_RESERVE) in the output.  This is usually only
        useful if there is virtual address exhaustion.

    --tagReserve
        If this flag is set, then !maddress will attempt to ""blame"" reserve segments
        on the region that immediately proceeded it.  For example, if a ""Heap""
        memory segment is immediately followed by a MEM_RESERVE region, we will call
        that reserve region HeapReserve.  Note that this is a heuristic and NOT
        intended to be completely accurate.  This can be useful to try to figure out
        what is creating large amount of MEM_RESERVE regions.
";
        }
    }
}
