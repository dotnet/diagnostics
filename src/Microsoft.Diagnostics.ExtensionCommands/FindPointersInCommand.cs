// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.NativeAddressHelper;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "findpointersin", Help = "Finds pointers to the GC heap within the given memory regions.")]
    public sealed class FindPointersInCommand : CommandBase
    {
        [ServiceImport]
        public IModuleService ModuleService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public NativeAddressHelper AddressHelper { get; set; }

        [Option(Name = "--showAllObjects", Aliases = new string[] { "-a", "--all" }, Help = "Show all objects instead of only objects Pinned on the heap.")]
        public bool ShowAllObjects { get; set; }

        [Argument(Help = "The types of memory to search for pointers to the GC heap.")]
        public string[] Regions { get; set; }

        private const string VtableConst = "vtable for ";

        private int Width
        {
            get
            {
                int width = Console.WindowWidth;
                if (width == 0)
                {
                    width = 120;
                }

                if (width > 256)
                {
                    width = 256;
                }

                return width;
            }
        }

        public override void Invoke()
        {
            if (Regions is null || Regions.Length == 0)
            {
                throw new DiagnosticsException("Must specify at least one memory region type to search for.");
            }

            PrintPointers(!ShowAllObjects, Regions);
        }

        private void PrintPointers(bool pinnedOnly, params string[] memTypes)
        {
            DescribedRegion[] allRegions = AddressHelper.EnumerateAddressSpace(tagClrMemoryRanges: true, includeReserveMemory: false, tagReserveMemoryHeuristically: false, includeHandleTableIfSlow: false).ToArray();

            WriteLine("Scanning for pinned objects...");
            MemoryWalkContext ctx = CreateMemoryWalkContext();

            foreach (string type in memTypes)
            {
                DescribedRegion[] matchingRanges = allRegions.Where(r => r.Name == type).ToArray();
                if (matchingRanges.Length == 0)
                {
                    WriteLine($"Found not memory regions matching '{type}'.");
                    continue;
                }

                RegionPointers totals = new();

                foreach (DescribedRegion mem in matchingRanges.OrderBy(r => r.Start))
                {
                    IEnumerable<(ulong Pointer, DescribedRegion MemoryRange)> pointersFound = AddressHelper.EnumerateRegionPointers(mem.Start, mem.End, allRegions).Select(r => (r.Pointer, r.MemoryRange));
                    RegionPointers result = ProcessOneRegion(pinnedOnly, pointersFound, ctx);

                    WriteMemoryHeaderLine(mem);

                    WriteLine($"Type:  {mem.Name}");
                    if (mem.Image is not null)
                    {
                        WriteLine($"Image: {mem.Image}");
                    }

                    WriteTables(ctx, result, false);

                    WriteLine("");
                    WriteLine("END REGION".PadLeft((Width - 10) / 2, '=').PadRight(Width, '='));
                    WriteLine("");

                    result.AddTo(totals);
                }

                if (matchingRanges.Length > 1)
                {
                    WriteLine(" TOTALS ".PadLeft(Width / 2).PadRight(Width));

                    WriteTables(ctx, totals, truncate: true);

                    WriteLine(new string('=', Width));
                }
            }
        }

        private void WriteTables(MemoryWalkContext ctx, RegionPointers result, bool truncate)
        {
            if (result.PointersToGC > 0)
            {
                WriteLine("");
                WriteLine("Pointers to GC heap:");

                PrintGCPointerTable(result);
            }

            if (result.ResolvablePointers.Count > 0)
            {
                WriteLine("");
                WriteLine("Pointers to images with symbols:");

                WriteResolvablePointerTable(ctx, result, truncate);
            }

            if (result.UnresolvablePointers.Count > 0)
            {
                WriteLine("");
                WriteLine("Other pointers:");

                WriteUnresolvablePointerTable(result, truncate);
            }
        }

        private void WriteMemoryHeaderLine(DescribedRegion mem)
        {
            string header = $"REGION [{mem.Start:x}-{mem.End:x}] {mem.Type} {mem.State} {mem.Protection}";
            int lpad = (Width - header.Length) / 2;
            if (lpad > 0)
            {
                header = header.PadLeft(Width - lpad, '=');
            }

            WriteLine(header.PadRight(Width, '='));
        }

        private void PrintGCPointerTable(RegionPointers result)
        {
            if (result.PinnedPointers.Count == 0)
            {
                WriteLine($"{result.PointersToGC:n0} pointers to the GC heap, but none pointed to a pinned object.");
            }
            else
            {
                IEnumerable<(string Key, int, int, IEnumerable<ulong>)> gcResult = from obj in result.PinnedPointers
                                                                                   let name = obj.Type?.Name ?? "<unknown_object_types>"
                                                                                   group obj.Address by name into g
                                                                                   let Count = g.Count()
                                                                                   orderby Count descending
                                                                                   select
                                                                                   (
                                                                                       g.Key,
                                                                                       Count,
                                                                                       new HashSet<ulong>(g).Count,
                                                                                       g.AsEnumerable()
                                                                                   );

                if (result.NonPinnedGCPointers.Count > 0)
                {
                    (string, int, int, IEnumerable<ulong>)[] v = new (string, int, int, IEnumerable<ulong>)[] { ("[Pointers to non-pinned objects]", result.NonPinnedGCPointers.Count, new HashSet<ulong>(result.NonPinnedGCPointers).Count, result.NonPinnedGCPointers) };
                    gcResult = v.Concat(gcResult);
                }

                PrintPointerTable("Type", "[Other Pinned Object Pointers]", forceTruncate: false, gcResult);
            }
        }

        private void WriteUnresolvablePointerTable(RegionPointers result, bool forceTruncate)
        {
            IEnumerable<(string Key, int, int, IEnumerable<ulong>)> unresolvedQuery = from item in result.UnresolvablePointers
                                                                                      let Name = item.Key.Image ?? item.Key.Name
                                                                                      group item.Value by Name into g
                                                                                      let All = g.SelectMany(r => r).ToArray()
                                                                                      let Count = All.Length
                                                                                      orderby Count descending
                                                                                      select
                                                                                      (
                                                                                          g.Key,
                                                                                          Count,
                                                                                          new HashSet<ulong>(All).Count,
                                                                                          All.AsEnumerable()
                                                                                      );


            PrintPointerTable("Region", "[Unique Pointers to Unique Regions]", forceTruncate, unresolvedQuery);
        }

        private void WriteResolvablePointerTable(MemoryWalkContext ctx, RegionPointers result, bool forceTruncate)
        {
            IEnumerable<(string, int, int, IEnumerable<ulong>)> resolvedQuery = from ptr in result.ResolvablePointers.SelectMany(r => r.Value)
                                                                                let r = ctx.ResolveSymbol(ModuleService, ptr)
                                                                                let name = r.Symbol ?? "<unknown_function>"
                                                                                group (ptr, r.Offset) by name into g
                                                                                let Count = g.Count()
                                                                                let UniqueOffsets = new HashSet<int>(g.Select(g => g.Offset))
                                                                                orderby Count descending
                                                                                select
                                                                                (
                                                                                    FixTypeName(g.Key, UniqueOffsets),
                                                                                    Count,
                                                                                    UniqueOffsets.Count,
                                                                                    g.Select(r => r.ptr)
                                                                                );

            PrintPointerTable("Symbol", "[Unique Pointers]", forceTruncate, resolvedQuery);
        }

        private void PrintPointerTable(string nameColumn, string truncatedName, bool forceTruncate, IEnumerable<(string Name, int Count, int Unique, IEnumerable<ulong> Pointers)> query)
        {
            (string Name, int Count, int Unique, IEnumerable<ulong> Pointers)[] resolved = query.ToArray();
            if (resolved.Length == 0)
            {
                return;
            }

            int single = resolved.Count(r => r.Count == 1);
            int multi = resolved.Length - single;
            bool truncate = forceTruncate || (single + multi > 75 && single > multi);
            truncate = false;

            int maxNameLen = multi > 0 ? resolved.Where(r => !truncate || r.Count > 1).Max(r => r.Name.Length) : resolved.Max(r => r.Name.Length);
            int nameLen = Math.Min(80, maxNameLen);
            nameLen = Math.Max(nameLen, truncatedName.Length);

            Table table = new(Console, TypeName.WithWidth(nameLen), Integer, Integer, Pointer)
            {
                Border = true
            };

            table.Columns[0] = table.Columns[0].WithAlignment(Align.Center);
            table.WriteHeader(nameColumn, "Unique", "Count", "RndPtr");
            table.Columns[0] = table.Columns[0].WithAlignment(Align.Left);

            IEnumerable<(string Name, int Count, int Unique, IEnumerable<ulong> Pointers)> items = truncate ? resolved.Take(multi) : resolved;
            foreach ((string Name, int Count, int Unique, IEnumerable<ulong> Pointers) in items)
            {
                table.WriteRow(Name, Unique, Count, Pointers.FindMostCommonPointer());
            }

            if (truncate)
            {
                table.WriteRow(truncatedName, single, single);
            }

            table.Columns[0] = table.Columns[0].WithAlignment(Align.Center);
            table.WriteFooter("TOTALS", resolved.Sum(r => r.Unique), resolved.Sum(r => r.Count));
        }

        private static string FixTypeName(string typeName, HashSet<int> offsets)
        {
            if (typeName.EndsWith("!") && typeName.Count(r => r == '!') == 1)
            {
                typeName = typeName.Substring(0, typeName.Length - 1);
            }

            int vtableIdx = typeName.IndexOf(VtableConst, StringComparison.InvariantCulture);
            if (vtableIdx > 0)
            {
                typeName = typeName.Replace(VtableConst, "") + "::vtable";
            }

            if (offsets.Count == 1 && offsets.Single() > 0)
            {
                typeName = $"{typeName}+{offsets.Single():x}";
            }
            else if (offsets.Count > 1)
            {
                typeName = $"{typeName}+...";
            }

            return typeName;
        }

        private RegionPointers ProcessOneRegion(bool pinnedOnly, IEnumerable<(ulong Pointer, DescribedRegion Range)> pointersFound, MemoryWalkContext ctx)
        {
            RegionPointers result = new();

            foreach ((ulong Pointer, DescribedRegion Range) found in pointersFound)
            {
                if (found.Range.ClrMemoryKind == ClrMemoryKind.GCHeap)
                {
                    if (pinnedOnly)
                    {
                        if (ctx.IsPinnedObject(found.Pointer, out ClrObject obj))
                        {
                            result.AddGCPointer(obj);
                        }
                        else
                        {
                            result.AddGCPointer(found.Pointer);
                        }
                    }
                    else
                    {
                        ClrObject obj = Runtime.Heap.GetObject(found.Pointer);
                        if (obj.IsValid)
                        {
                            result.AddGCPointer(obj);
                        }
                    }
                }
                else if (found.Range.Type == MemoryRegionType.MEM_IMAGE)
                {
                    bool hasSymbols = false;
                    IModuleSymbols symbols = found.Range.Module?.Services.GetService<IModuleSymbols>();
                    if (symbols is not null)
                    {
                        hasSymbols = symbols.GetSymbolStatus() == SymbolStatus.Loaded;
                    }

                    result.AddRegionPointer(found.Range, found.Pointer, hasSymbols);
                }
                else
                {
                    result.AddRegionPointer(found.Range, found.Pointer, hasSymbols: false);
                }
            }

            return result;
        }

        private MemoryWalkContext CreateMemoryWalkContext()
        {
            HashSet<ulong> seen = new();
            List<ClrObject> pinned = new();

            foreach (ClrRoot root in Runtime.Heap.EnumerateRoots().Where(r => r.IsPinned))
            {
                if (root.Object.IsValid && !root.Object.IsFree)
                {
                    if (seen.Add(root.Object))
                    {
                        pinned.Add(root.Object);
                    }
                }
            }

            foreach (ClrSegment seg in Runtime.Heap.Segments.Where(s => s.IsPinned))
            {
                foreach (ClrObject obj in seg.EnumerateObjects().Where(o => seen.Add(o)))
                {
                    if (!obj.IsFree && obj.IsValid)
                    {
                        pinned.Add(obj);
                    }
                }
            }

            return new MemoryWalkContext(pinned);
        }

        private sealed class RegionPointers
        {
            public Dictionary<DescribedRegion, List<ulong>> ResolvablePointers { get; } = new();
            public Dictionary<DescribedRegion, List<ulong>> UnresolvablePointers { get; } = new();
            public List<ClrObject> PinnedPointers { get; } = new();
            public List<ulong> NonPinnedGCPointers { get; } = new();
            public long PointersToGC => PinnedPointers.Count + NonPinnedGCPointers.Count;

            public RegionPointers()
            {
            }

            public void AddGCPointer(ulong address)
            {
                NonPinnedGCPointers.Add(address);
            }

            public void AddGCPointer(ClrObject obj)
            {
                PinnedPointers.Add(obj);
            }

            internal void AddRegionPointer(DescribedRegion range, ulong pointer, bool hasSymbols)
            {
                Dictionary<DescribedRegion, List<ulong>> pointerMap = hasSymbols ? ResolvablePointers : UnresolvablePointers;

                if (!pointerMap.TryGetValue(range, out List<ulong> pointers))
                {
                    pointers = pointerMap[range] = new();
                }

                pointers.Add(pointer);
            }

            public void AddTo(RegionPointers destination)
            {
                AddTo(ResolvablePointers, destination.ResolvablePointers);
                AddTo(UnresolvablePointers, destination.UnresolvablePointers);
                destination.PinnedPointers.AddRange(PinnedPointers);
                destination.NonPinnedGCPointers.AddRange(NonPinnedGCPointers);
            }

            private static void AddTo(Dictionary<DescribedRegion, List<ulong>> sourceDict, Dictionary<DescribedRegion, List<ulong>> destDict)
            {
                foreach (KeyValuePair<DescribedRegion, List<ulong>> item in sourceDict)
                {
                    if (destDict.TryGetValue(item.Key, out List<ulong> values))
                    {
                        values.AddRange(item.Value);
                    }
                    else
                    {
                        destDict[item.Key] = new(item.Value);
                    }
                }
            }
        }

        private sealed class MemoryWalkContext
        {
            private readonly Dictionary<ulong, (string, int)> _resolved = new();
            private readonly ClrObject[] _pinned;

            public MemoryWalkContext(IEnumerable<ClrObject> pinnedObjects)
            {
                _pinned = pinnedObjects.Where(o => o.IsValid && !o.IsFree).OrderBy(o => o.Address).ToArray();
            }

            public bool IsPinnedObject(ulong address, out ClrObject found)
            {
                ClrObject last = _pinned.LastOrDefault();
                if (_pinned.Length == 0 || address < _pinned[0].Address || address >= last.Address + last.Size)
                {
                    found = default;
                    return false;
                }

                int low = 0;
                int high = _pinned.Length - 1;
                while (low <= high)
                {
                    int mid = (low + high) >> 1;
                    if (_pinned[mid].Address + _pinned[mid].Size <= address)
                    {
                        low = mid + 1;
                    }
                    else if (address < _pinned[mid].Address)
                    {
                        high = mid - 1;
                    }
                    else
                    {
                        found = _pinned[mid];
                        return true;
                    }
                }

                found = default;
                return false;
            }

            public (string Symbol, int Offset) ResolveSymbol(IModuleService modules, ulong ptr)
            {
                if (_resolved.TryGetValue(ptr, out (string, int) result))
                {
                    return result;
                }

                // _resolved is just a cache.  Don't let it get so big we eat all of the memory.
                if (_resolved.Count > 16 * 1024)
                {
                    _resolved.Clear();
                }

                IModule module = modules.GetModuleFromAddress(ptr);
                IModuleSymbols symbols = module?.Services.GetService<IModuleSymbols>();

                if (symbols is not null && symbols.TryGetSymbolName(ptr, out string symbolName, out ulong displacement))
                {
                    string moduleName = module.FileName;
                    if (!string.IsNullOrWhiteSpace(moduleName))
                    {
                        symbolName = Path.GetFileName(moduleName) + "!" + symbolName;
                    }

                    return _resolved[ptr] = (symbolName, displacement > int.MaxValue ? int.MaxValue : (int)displacement);
                }

                return (null, -1);
            }
        }

        [HelpInvoke]
        public void HelpInvoke()
        {
            WriteLine(
@"-------------------------------------------------------------------------------
The findpointersin command will search the regions of memory given by MADDRESS_TYPE_LIST
to find all pointers to other memory regions and display them.  By default, pointers
to the GC heap are only displayed if the object it points to is pinned.  (As any
random pointer to the GC heap to a non-pinned object is either an old/leftover
pointer, or a stray pointer that should be ignored.) If --all is set,
then this command print out ALL objects that are pointed to instead of collapsing
them into one entry.

usage: !findpointersin [--all] MADDRESS_TYPE_LIST

Note: The MADDRESS_TYPE_LIST must be a memory type as printed by !maddress.

Example: ""!findpointersin PAGE_READWRITE"" will only search for regions of memory that
!maddress marks as ""PAGE_READWRITE"" and not every page of memory that's
marked with PAGE_READWRITE protection.

Example: Running the command ""!findpointersin Stack PAGE_READWRITE"" will find all pointers
on any ""Stack"" and ""PAGE_READWRITE"" memory segments and summarize those contents into
three tables:  One table for pointers to the GC heap, one table for pointers where
symbols could be resolved, and one table of pointers where we couldn't resolve symbols.


Sample Output:

    Pointers to GC heap:
    -------------------------------Type---------Unique----------Count---------RndPtr
       [Pointers to non-pinned objects]          3,168         16,765   7f05b80d5b60
                        System.Object[]              3             58   7f07380f3120
                          System.Byte[]              7             22   7f07380f00d8
    Microsoft.Caching.ClrMD.RawResult[]              2             14   7f063822ae58
    ----------------------- [ TOTALS ] ----------3,180---------16,859---------------

    Pointers to images:
    --------------------------------------------------------------------------Symbol---------Unique----------Count---------RndPtr
                                                                      libcoreclr+...             34            457   7f08c66ff776
                                       libcoreclr!JIT_GetSharedGCThreadStaticBase+33              1            260   7f08c637b453
                                        libcoreclr!COMInterlocked::ExchangeObject+17              1            258   7f08c6336597

                                        ...
    -------------------------------------------------------------------- [ TOTALS ] ------------740---------10,361---------------

    Other pointers:
    ---------------------------------------------------------------Region---------Unique----------Count---------RndPtr
                                                                    Stack         25,229         37,656   7f05297f4738
                                                           PAGE_READWRITE          1,696          7,882   7f0500000000
                                                         LowFrequencyHeap          2,618          7,347   7f084d1868e0

                                                         ...
    --------------------------------------------------------- [ TOTALS ] ---------33,360---------72,029---------------
");
        }
    }
}
