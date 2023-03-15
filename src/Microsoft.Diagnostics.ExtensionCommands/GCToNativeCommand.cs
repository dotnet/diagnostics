// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.NativeAddressHelper;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "gctonative", Help = "Finds GC objects which point to the given native memory ranges.")]
    public sealed class GCToNativeCommand : ClrRuntimeCommandBase
    {
        [Argument(Help = "The types of memory to search the GC heap for.")]
        public string[] MemoryTypes { get; set; }

        [Option(Name = "--all", Aliases = new string[] { "-a" }, Help = "Show the complete list of objects and not just a summary.")]
        public bool ShowAll { get; set; }

        [ServiceImport(Optional = true)]
        public NativeAddressHelper AddressHelper { get; set; }

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

        public override void ExtensionInvoke()
        {
            if (AddressHelper == null)
            {
                throw new DiagnosticsException("The memory region service does not exists. This command is only supported under windbg/cdb debuggers.");
            }

            if (MemoryTypes is null || MemoryTypes.Length == 0)
            {
                throw new DiagnosticsException("Must specify at least one memory region type to search for.");
            }

            PrintGCPointersToMemory(ShowAll, MemoryTypes);
        }

        public void PrintGCPointersToMemory(bool showAll, params string[] memoryTypes)
        {
            // Strategy:
            //   1. Use ClrMD to get the bounds of the GC heap where objects are allocated.
            //   2. Manually read that memory and check every pointer-aligned address for pointers to the heap regions requested
            //      while recording pointers in a list as we go (along with which ClrSegment they came from).
            //   3. Walk each GC segment which has pointers to the target regions to find objects:
            //        a.  Annotate each target pointer so we know what object points to the region (and throw away any pointers
            //            that aren't in an object...those are pointers from dead or relocated objects).
            //        b.  We have some special knowledge of "well known types" here that contain pointers.  These types point to
            //            native memory and contain a size of the region they point to.  Record that information as we go.
            //   4. Use information from "well known types" about regions of memory to annotate other pointers that do not have
            //      size information.
            //   5. Display all of this to the user.

            if (memoryTypes.Length == 0)
            {
                return;
            }

            IEnumerable<DescribedRegion> rangeEnum = AddressHelper.EnumerateAddressSpace(tagClrMemoryRanges: true, includeReserveMemory: false, tagReserveMemoryHeuristically: false, includeHandleTableIfSlow: false);
            rangeEnum = rangeEnum.Where(r => memoryTypes.Any(memType => r.Name.Equals(memType, StringComparison.OrdinalIgnoreCase)));
            rangeEnum = rangeEnum.OrderBy(r => r.Start);

            DescribedRegion[] ranges = rangeEnum.ToArray();

            if (ranges.Length == 0)
            {
                Console.WriteLine($"No matching memory ranges.");
                Console.WriteLine("");
                return;
            }

            Console.WriteLine("Walking GC heap to find pointers...");
            Dictionary<ClrSegment, List<GCObjectToRange>> segmentLists = new();

            IEnumerable<(ClrSegment Segment, ulong Address, ulong Pointer, DescribedRegion MemoryRange)> items = Runtime.Heap.Segments
                            .SelectMany(Segment => AddressHelper
                                                    .EnumerateRegionPointers(Segment.ObjectRange.Start, Segment.ObjectRange.End, ranges)
                                                    .Select(regionPointer => (Segment, regionPointer.Address, regionPointer.Pointer, regionPointer.MemoryRange)));

            foreach ((ClrSegment Segment, ulong Address, ulong Pointer, DescribedRegion MemoryRange) item in items)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (!segmentLists.TryGetValue(item.Segment, out List<GCObjectToRange> list))
                {
                    list = segmentLists[item.Segment] = new();
                }

                list.Add(new GCObjectToRange(item.Address, item.Pointer, item.MemoryRange));
            }

            Console.WriteLine("Resolving object names...");
            foreach (string type in memoryTypes)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                WriteHeader($" {type} Regions ");

                List<ulong> addressesNotInObjects = new();
                List<(ulong Pointer, ClrObject Object)> unknownObjPointers = new();
                Dictionary<ulong, KnownClrMemoryPointer> knownMemory = new();
                Dictionary<ulong, int> sizeHints = new();

                foreach (KeyValuePair<ClrSegment, List<GCObjectToRange>> segEntry in segmentLists)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    ClrSegment seg = segEntry.Key;
                    List<GCObjectToRange> pointers = segEntry.Value;
                    pointers.Sort((x, y) => x.GCPointer.CompareTo(y.GCPointer));

                    int index = 0;
                    foreach (ClrObject obj in seg.EnumerateObjects())
                    {
                        if (index >= pointers.Count)
                        {
                            break;
                        }

                        while (index < pointers.Count && pointers[index].GCPointer < obj.Address)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();

                            // If we "missed" the pointer then it's outside of an object range.
                            addressesNotInObjects.Add(pointers[index].GCPointer);

                            Trace.WriteLine($"Skipping {pointers[index].GCPointer:x} lastObj={obj.Address:x}-{obj.Address + obj.Size:x} {obj.Type?.Name}");

                            index++;
                        }

                        if (index == pointers.Count)
                        {
                            break;
                        }

                        while (index < pointers.Count && obj.Address <= pointers[index].GCPointer && pointers[index].GCPointer < obj.Address + obj.Size)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();

                            string typeName = obj.Type?.Name ?? $"<unknown_type>";

                            if (obj.IsFree)
                            {
                                // This is free space, if we found a pointer here then it was likely just relocated and we'll mark it elsewhere
                            }
                            else if (pointers[index].NativeMemoryRange.Name != type)
                            {
                                // This entry is for a different memory type, we'll get it on another pass
                            }
                            else if (knownMemory.ContainsKey(obj))
                            {
                                // do nothing, we already marked this memory
                            }
                            else if (KnownClrMemoryPointer.ContainsKnownClrMemoryPointers(obj))
                            {
                                foreach (KnownClrMemoryPointer knownMem in KnownClrMemoryPointer.EnumerateKnownClrMemoryPointers(obj, sizeHints))
                                {
                                    knownMemory.Add(obj, knownMem);
                                }
                            }
                            else
                            {
                                if (typeName.Contains('>'))
                                {
                                    typeName = CollapseGenerics(typeName);
                                }

                                unknownObjPointers.Add((pointers[index].TargetSegmentPointer, obj));
                            }

                            index++;
                        }
                    }
                }

                Console.WriteLine("");
                if (knownMemory.Count == 0 && unknownObjPointers.Count == 0)
                {
                    Console.WriteLine($"No GC heap pointers to '{type}' regions.");
                }
                else
                {
                    if (showAll)
                    {
                        Console.WriteLine($"All memory pointers:");

                        IEnumerable<(ulong Pointer, ulong Size, ClrObject Object, ClrType Type)> allPointers = unknownObjPointers.Select(unknown => (unknown.Pointer, 0ul, unknown.Object, unknown.Object.Type));
                        allPointers = allPointers.Concat(knownMemory.Values.Select(k => (k.Pointer, GetSize(sizeHints, k), k.Object, k.Object.Type)));

                        using BorderedTable allOut = new(Console, Pointer, ByteCount, DumpObj, TypeName);

                        allOut.WriteHeader("Pointer", "Size", "Object", "Type");

                        foreach ((ulong Pointer, ulong Size, ClrObject Object, ClrType Type) entry in allPointers)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();

                            if (entry.Size == 0)
                            {
                                allOut.WriteRow(entry.Pointer, null, entry.Object, entry.Type);
                            }
                            else
                            {
                                allOut.WriteRow(entry.Pointer, entry.Size, entry.Object, entry.Type);
                            }
                        }

                        Console.WriteLine("");
                    }

                    if (knownMemory.Count > 0)
                    {
                        Console.WriteLine($"Well-known memory pointer summary:");

                        // totals
                        var knownMemorySummary = from known in knownMemory.Values
                                                 group known by known.Object.Type into g
                                                 let Type = g.Key
                                                 let Count = g.Count()
                                                 let TotalSize = g.Sum(k => (long)GetSize(sizeHints, k))
                                                 orderby TotalSize descending, Type.Name ascending
                                                 select new {
                                                     Type,
                                                     Count,
                                                     TotalSize,
                                                     Pointer = g.Select(p => p.Pointer).FindMostCommonPointer()
                                                 };

                        Column typeNameColumn = TypeName.GetAppropriateWidth(knownMemory.Values.Select(r => r.Object.Type), 16);
                        using (BorderedTable summary = new(Console, typeNameColumn, Integer, HumanReadableSize, ByteCount, Pointer))
                        {
                            summary.WriteHeader("Type", "Count", "Size", "Size (bytes)", "RndPointer");

                            foreach (var item in knownMemorySummary)
                            {
                                Console.CancellationToken.ThrowIfCancellationRequested();

                                summary.WriteRow(item.Type, item.Count, item.TotalSize, item.TotalSize, item.Pointer);
                            }

                            (int totalRegions, ulong totalBytes) = GetSizes(knownMemory, sizeHints);
                            summary.WriteFooter("[TOTAL]", totalRegions, totalBytes, totalBytes);
                        }

                        Console.WriteLine();
                    }


                    if (unknownObjPointers.Count > 0)
                    {
                        Console.WriteLine($"Other memory pointer summary:");

                        var unknownMemQuery = from known in unknownObjPointers
                                              let name = CollapseGenerics(known.Object.Type?.Name ?? "<unknown_type>")
                                              group known by name into g
                                              let Name = g.Key
                                              let Count = g.Count()
                                              orderby Count descending
                                              select new {
                                                  Name,
                                                  Count,
                                                  Pointer = g.Select(p => p.Pointer).FindMostCommonPointer()
                                              };

                        var unknownMem = unknownMemQuery.ToArray();

                        Column typeNameColumn = TypeName.GetAppropriateWidth(unknownMem.Select(r => r.Name));
                        using BorderedTable summary = new(Console, typeNameColumn, Integer, Pointer);
                        summary.WriteHeader("Type", "Count", "RndPointer");

                        foreach (var item in unknownMem)
                        {
                            Console.CancellationToken.ThrowIfCancellationRequested();

                            summary.WriteRow(item.Name, item.Count, item.Pointer);
                        }
                    }
                }
            }
        }

        private static (int Regions, ulong Bytes) GetSizes(Dictionary<ulong, KnownClrMemoryPointer> knownMemory, Dictionary<ulong, int> sizeHints)
        {
            IOrderedEnumerable<KnownClrMemoryPointer> ordered = from item in knownMemory.Values
                                                                orderby item.Pointer ascending, item.Size descending
                                                                select item;

            int totalRegions = 0;
            ulong totalBytes = 0;
            ulong prevEnd = 0;

            foreach (KnownClrMemoryPointer item in ordered)
            {
                ulong size = GetSize(sizeHints, item);

                // overlapped pointer
                if (item.Pointer < prevEnd)
                {
                    if (item.Pointer + size <= prevEnd)
                    {
                        continue;
                    }

                    ulong diff = prevEnd - item.Pointer;
                    if (diff >= size)
                    {
                        continue;
                    }

                    size -= diff;
                    prevEnd += size;
                }
                else
                {
                    totalRegions++;
                    prevEnd = item.Pointer + size;
                }

                totalBytes += size;
            }

            return (totalRegions, totalBytes);
        }

        private void WriteHeader(string header)
        {
            int lpad = (Width - header.Length) / 2;
            if (lpad > 0)
            {
                header = header.PadLeft(Width - lpad, '=');
            }

            Console.WriteLine(header.PadRight(Width, '='));
        }

        private string CollapseGenerics(string typeName)
        {
            StringBuilder result = new(typeName.Length + 16);
            int nest = 0;
            for (int i = 0; i < typeName.Length; i++)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (typeName[i] == '<')
                {
                    if (nest++ == 0)
                    {
                        if (i < typeName.Length - 1 && typeName[i + 1] == '>')
                        {
                            result.Append("<>");
                        }
                        else
                        {
                            result.Append("<...>");
                        }
                    }
                }
                else if (typeName[i] == '>')
                {
                    nest--;
                }
                else if (nest == 0)
                {
                    result.Append(typeName[i]);
                }
            }

            return result.ToString();
        }

        private static ulong GetSize(Dictionary<ulong, int> sizeHints, KnownClrMemoryPointer k)
        {
            if (sizeHints.TryGetValue(k.Pointer, out int hint))
            {
                if ((ulong)hint > k.Size)
                {
                    return (ulong)hint;
                }
            }

            return k.Size;
        }

        private sealed class GCObjectToRange
        {
            public ulong GCPointer { get; }
            public ulong TargetSegmentPointer { get; }
            public ClrObject Object { get; set; }
            public DescribedRegion NativeMemoryRange { get; }

            public GCObjectToRange(ulong gcaddr, ulong pointer, DescribedRegion nativeMemory)
            {
                GCPointer = gcaddr;
                TargetSegmentPointer = pointer;
                NativeMemoryRange = nativeMemory;
            }
        }

        private sealed class KnownClrMemoryPointer
        {
            private const string NativeHeapMemoryBlock = "System.Reflection.Internal.NativeHeapMemoryBlock";
            private const string MetadataReader = "System.Reflection.Metadata.MetadataReader";
            private const string NativeHeapMemoryBlockDisposableData = "System.Reflection.Internal.NativeHeapMemoryBlock+DisposableData";
            private const string ExternalMemoryBlockProvider = "System.Reflection.Internal.ExternalMemoryBlockProvider";
            private const string ExternalMemoryBlock = "System.Reflection.Internal.ExternalMemoryBlock";
            private const string RuntimeParameterInfo = "System.Reflection.RuntimeParameterInfo";

            public ClrObject Object { get; }
            public ulong Pointer { get; }
            public ulong Size { get; }

            public KnownClrMemoryPointer(ClrObject obj, nint pointer, int size)
            {
                Object = obj;
                Pointer = (ulong)pointer;
                Size = (ulong)size;
            }

            public static bool ContainsKnownClrMemoryPointers(ClrObject obj)
            {
                string typeName = obj.Type?.Name;
                return typeName is NativeHeapMemoryBlock
                    or MetadataReader
                    or NativeHeapMemoryBlockDisposableData
                    or ExternalMemoryBlockProvider
                    or ExternalMemoryBlock
                    or RuntimeParameterInfo
                    ;
            }

            public static IEnumerable<KnownClrMemoryPointer> EnumerateKnownClrMemoryPointers(ClrObject obj, Dictionary<ulong, int> sizeHints)
            {
                switch (obj.Type?.Name)
                {
                    case RuntimeParameterInfo:
                        {
                            const int MDInternalROSize = 0x5f8; // Doesn't have to be exact
                            nint pointer = obj.ReadValueTypeField("m_scope").ReadField<nint>("m_metadataImport2");
                            AddSizeHint(sizeHints, pointer, MDInternalROSize);

                            yield return new KnownClrMemoryPointer(obj, pointer, MDInternalROSize);
                        }
                        break;
                    case ExternalMemoryBlock:
                        {
                            nint pointer = obj.ReadField<nint>("_buffer");
                            int size = obj.ReadField<int>("_size");

                            if (pointer != 0 && size > 0)
                            {
                                AddSizeHint(sizeHints, pointer, size);
                            }

                            yield return new KnownClrMemoryPointer(obj, pointer, size);
                        }
                        break;

                    case ExternalMemoryBlockProvider:
                        {
                            nint pointer = obj.ReadField<nint>("_memory");
                            int size = obj.ReadField<int>("_size");

                            if (pointer != 0 && size > 0)
                            {
                                AddSizeHint(sizeHints, pointer, size);
                            }

                            yield return new KnownClrMemoryPointer(obj, pointer, size);
                        }
                        break;

                    case NativeHeapMemoryBlockDisposableData:
                        {
                            nint pointer = obj.ReadField<nint>("_pointer");
                            sizeHints.TryGetValue((ulong)pointer, out int size);
                            yield return new KnownClrMemoryPointer(obj, pointer, size);
                        }
                        break;

                    case NativeHeapMemoryBlock:
                        {
                            // Just here for size hints

                            ClrObject pointerObject = obj.ReadObjectField("_data");
                            nint pointer = pointerObject.ReadField<nint>("_pointer");
                            int size = obj.ReadField<int>("_size");

                            if (pointer != 0 && size > 0)
                            {
                                AddSizeHint(sizeHints, pointer, size);
                            }
                        }

                        break;

                    case MetadataReader:
                        {
                            MemoryBlockImpl block = obj.ReadField<MemoryBlockImpl>("Block");
                            if (block.Pointer != 0 && block.Size > 0)
                            {
                                yield return new KnownClrMemoryPointer(obj, block.Pointer, block.Size);
                            }
                        }
                        break;
                }
            }

            private static void AddSizeHint(Dictionary<ulong, int> sizeHints, nint pointer, int size)
            {
                if (pointer != 0 && size != 0)
                {
                    ulong ptr = (ulong)pointer;

                    if (sizeHints.TryGetValue(ptr, out int hint))
                    {
                        if (hint < size)
                        {
                            sizeHints[ptr] = size;
                        }
                    }
                    else
                    {
                        sizeHints[ptr] = size;
                    }
                }
            }

            private readonly struct MemoryBlockImpl
            {
                public readonly nint Pointer { get; }
                public readonly int Size { get; }
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
gctonative searches the GC heap for pointers to native memory.  This is used
to help locate regions of native memory that are referenced (or possibly held
alive) by objects on the GC heap.

usage: gctonative [--all] MADDRESS_TYPE_LIST

Note: The MADDRESS_TYPE_LIST must be a memory type as printed by maddress.

If --all is set, a full list of every pointer from the GC heap to the
specified memory will be displayed instead of just a summary table.

Sample Output:

    0:000> gctonative PAGE_READWRITE
    Walking GC heap to find pointers...
    Resolving object names...
    ================================================ PAGE_READWRITE Regions ================================================

    Well-known memory pointer summary:
    Type-----------------------------------------------------------------Count-----------Size---Size (bytes)-----RndPointer
    System.Reflection.Internal.ExternalMemoryBlockProvider          |    1,956 |     571.39mb |  599,145,088 | 7f0478747cf0
    System.Reflection.Internal.NativeHeapMemoryBlock+DisposableData |    1,956 |     571.39mb |  599,145,088 | 7f0478747cf0
    System.Reflection.Internal.ExternalMemoryBlock                  |    1,956 |     161.63mb |  169,483,352 | 7f04898e06a0
    System.Reflection.Metadata.MetadataReader                       |    1,956 |     161.63mb |  169,483,352 | 7f04898e06a0
    System.Reflection.RuntimeParameterInfo                          |      176 |     262.63kb |      268,928 | 7f058000c220
    -----------------------------------------------------------------------------------------------------------------------
    [TOTAL]                                                         |    1,963 |     571.40mb |  599,155,784

    Other memory pointer summary:
    Type----------------------------------------------------------------------------------Count-----RndPointer
    System.SByte[]                                                                   |    1,511 | 7f0500000000
    System.Byte[]                                                                    |      539 | 7f0500000000
    System.Reflection.RuntimeAssembly                                                |      135 | 7f05a0000ce0
    System.Char[]                                                                    |      121 | 7f0500000000
    System.Threading.UnmanagedThreadPoolWorkItem                                     |      113 | 7f05800120e0
    System.Diagnostics.Tracing.EventSource+EventMetadata[]                           |       75 | 7f0564001a20
    Microsoft.Win32.SafeHandles.SafeEvpMdCtxHandle                                   |       56 | 7f044013c170
    System.Threading.Thread                                                          |       40 | 7f05741d7bd0
    System.Security.Cryptography.SafeEvpPKeyHandle                                   |       40 | 7f051400cca0
    Microsoft.Win32.SafeHandles.SafeBioHandle                                        |       38 | 7f05400078d0
    Microsoft.Win32.SafeHandles.SafeX509Handle                                       |       37 | 7f04beab9c30
    Microsoft.Win32.SafeHandles.SafeSslHandle                                        |       20 | 7f0540007af0
    System.Text.RegularExpressions.RegexCache+Node                                   |       19 | 7f0500000001
    System.Collections.Concurrent.ConcurrentDictionary<...>+Node                     |       19 | 7f0500000001
    System.Diagnostics.Tracing.EventPipeEventProvider                                |       15 | 7f0580002240
    System.IntPtr[]                                                                  |       15 | 7f0578000d00
    Microsoft.Extensions.Logging.LoggerMessage+LogValues<...>                        |       12 | 7f0500000002
    Microsoft.Extensions.Logging.LoggerMessage+LogValues<...>+<...>d__9              |       12 | 7f0500000002
    Microsoft.Win32.SafeHandles.SafeX509StackHandle                                  |       10 | 7f0524179e50
    Microsoft.CodeAnalysis.CSharp.Symbols.MethodSymbol+<...>d__32                    |       10 | 7f0500000002
    Microsoft.CodeAnalysis.ModuleMetadata[]                                          |        5 | 7f052a7f7050
    System.Threading.TimerQueue+AppDomainTimerSafeHandle                             |        2 | 7f05a0006d30
    System.Net.Security.SafeFreeCertContext                                          |        2 | 7f04beab9c30
    System.Threading.LowLevelLock                                                    |        1 | 7f05a0003420
    Microsoft.CodeAnalysis.CSharp.CSharpCompilation+ReferenceManager+AssemblyData... |        1 | 7f05800120e0
    System.Net.Sockets.SocketAsyncEngine                                             |        1 | 7f059800edd0
    Microsoft.Extensions.Caching.Memory.CacheEntry                                   |        1 | 7f05241e0000
    System.Runtime.CompilerServices.AsyncTaskMethodBuilder<...>+AsyncStateMachine... |        1 | 7f0500000004
";
    }
}
