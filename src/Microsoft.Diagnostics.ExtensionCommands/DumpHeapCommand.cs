// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpheap", Aliases = new[] { "DumpHeap" }, Help = "Displays a list of all managed objects.")]
    public class DumpHeapCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [ServiceImport]
        public DumpHeapService DumpHeap { get; set; }

        [Option(Name = "-mt")]
        public string MethodTableString { get; set; }

        private ulong? MethodTable { get; set; }

        [Option(Name = "-type")]
        public string Type { get; set; }

        [Option(Name = "-stat")]
        public bool StatOnly { get; set; }

        [Option(Name = "-strings")]
        public bool Strings { get; set; }

        [Option(Name = "-short")]
        public bool Short { get; set; }

        [Option(Name = "-min")]
        public ulong Min { get; set; }

        [Option(Name = "-max")]
        public ulong Max { get; set; }

        [Option(Name = "-live")]
        public bool Live { get; set; }

        [Option(Name = "-dead")]
        public bool Dead{ get; set; }

        [Option(Name = "-heap")]
        public int GCHeap { get; set; } = -1;

        [Option(Name = "-segment")]
        public string Segment { get; set; }

        [Option(Name = "-thinlock")]
        public bool ThinLock { get; set; }

        [Option(Name = "-gen")]
        public string Generation { get; set; }

        [Option(Name = "-ignoreGCState", Help = "Ignore the GC's marker that the heap is not walkable (will generate lots of false positive errors).")]
        public bool IgnoreGCState { get; set; }

        [Argument(Help = "Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        private HeapWithFilters FilteredHeap { get; set; }

        public override void Invoke()
        {
            ParseArguments();

            IEnumerable<ClrObject> objectsToPrint = FilteredHeap.EnumerateFilteredObjects(Console.CancellationToken);

            bool? liveObjectWarning = null;
            if ((Live || Dead) && Short)
            {
                liveObjectWarning = LiveObjects.PrintWarning;
                LiveObjects.PrintWarning = false;
            }

            if (Live)
            {
                objectsToPrint = objectsToPrint.Where(LiveObjects.IsLive);
            }
            else if (Dead)
            {
                objectsToPrint = objectsToPrint.Where(obj => !LiveObjects.IsLive(obj));
            }

            if (Type is not null)
            {
                objectsToPrint = objectsToPrint.Where(obj => obj.Type?.Name?.Contains(Type) ?? false);
            }

            if (MethodTable.HasValue)
            {
                objectsToPrint = objectsToPrint.Where(obj => {
                    ulong mt;
                    if (obj.Type is not null)
                    {
                        mt = obj.Type.MethodTable;
                    }
                    else
                    {
                        MemoryService.ReadPointer(obj, out mt);
                    }

                    return mt == MethodTable.Value;
                });
            }

            if (Min != 0 || Max != 0)
            {
                objectsToPrint = objectsToPrint.Where(obj =>
                {
                    // We cannot get the size of an invalid object
                    if (!obj.IsValid)
                    {
                        return false;
                    }

                    ulong size = obj.Size;
                    if (Min != 0 && size < Min)
                    {
                        return false;
                    }

                    if (Max != 0 && size > Max)
                    {
                        return false;
                    }

                    return true;
                });
            }

            bool printFragmentation = false;
            DumpHeapService.DisplayKind displayKind = DumpHeapService.DisplayKind.Normal;
            if (ThinLock)
            {
                displayKind = DumpHeapService.DisplayKind.ThinLock;
            }
            else if (Strings)
            {
                displayKind = DumpHeapService.DisplayKind.Strings;
            }
            else if (Short)
            {
                displayKind = DumpHeapService.DisplayKind.Short;
            }
            else
            {
                printFragmentation = true;
            }

            DumpHeap.PrintHeap(objectsToPrint, displayKind, StatOnly, printFragmentation);

            if (liveObjectWarning is bool original)
            {
                LiveObjects.PrintWarning = original;
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"Dumpheap is a powerful command that traverses the garbage collected heap, 
collection statistics about objects. With it's various options, it can look for
particular types, restrict to a range, or look for ThinLocks (see !SyncBlk 
documentation). Finally, it will provide a warning if it detects excessive 
fragmentation in the GC heap. 

When called without options, the output is first a list of objects in the heap,
followed by a report listing all the types found, their size and number:

    {prompt}dumpheap
     Address       MT     Size
    00a71000 0015cde8       12 Free
    00a7100c 0015cde8       12 Free
    00a71018 0015cde8       12 Free
    00a71024 5ba58328       68
    00a71068 5ba58380       68
    00a710ac 5ba58430       68
    00a710f0 5ba5dba4       68
    ...
    total 619 objects
    Statistics:
          MT    Count TotalSize Class Name
    5ba7607c        1        12 System.Security.Permissions.HostProtectionResource
    5ba75d54        1        12 System.Security.Permissions.SecurityPermissionFlag
    5ba61f18        1        12 System.Collections.CaseInsensitiveComparer
    ...
    0015cde8        6     10260      Free
    5ba57bf8      318     18136 System.String
    ...

""Free"" objects are simply regions of space the garbage collector can use later.
If 30% or more of the heap contains ""Free"" objects, the process may suffer from
heap fragmentation. This is usually caused by pinning objects for a long time 
combined with a high rate of allocation. Here is example output where 'dumpheap'
provides a warning about fragmentation:

    <After the Statistics section>
    Fragmented blocks larger than 1MB:
        Addr     Size Followed by
    00a780c0    1.5MB    00bec800 System.Byte[]
    00da4e38    1.2MB    00ed2c00 System.Byte[]
    00f16df0    1.2MB    01044338 System.Byte[]

The arguments in detail:

-stat     Restrict the output to the statistical type summary
-strings  Restrict the output to a statistical string value summary
-short    Limits output to just the address of each object. This allows you
          to easily pipe output from the command to another debugger 
          command for automation.
-min      Ignore objects less than the size given in bytes (hex)
-max      Ignore objects larger than the size given in bytes (hex)
-live     Only print live objects
-dead     Only print dead objects (objects which will be collected in the
          next full GC)
-thinlock Report on any ThinLocks (an efficient locking scheme, see 'syncblk'
          documentation for more info)
-startAtLowerBound 
          Force heap walk to begin at lower bound of a supplied address range.
          (During plan phase, the heap is often not walkable because objects 
          are being moved. In this case, 'dumpheap' may report spurious errors, 
          in particular bad objects. It may be possible to traverse more of 
          the heap after the reported bad object. Even if you specify an 
          address range, 'dumpheap' will start its walk from the beginning of 
          the heap by default. If it finds a bad object before the specified 
          range, it will stop before displaying the part of the heap in which 
          you are interested. This switch will force 'dumpheap' to begin its 
          walk at the specified lower bound. You must supply the address of a 
          good object as the lower bound for this to work. Display memory at 
          the address of the bad object to manually find the next method 
          table (use 'dumpmt' to verify). If the GC is currently in a call to 
          memcopy, You may also be able to find the next object's address by 
          adding the size to the start address given as parameters.) 
-mt       List only those objects with the MethodTable given
-type     List only those objects whose type name is a substring match of the 
          string provided. 
start     Begin listing from this address
end       Stop listing at this address

A special note about -type: Often, you'd like to find not only Strings, but
System.Object arrays that are constrained to contain Strings. (""new 
String[100]"" actually creates a System.Object array, but it can only hold
System.String object pointers). You can use -type in a special way to find
these arrays. Just pass ""-type System.String[]"" and those Object arrays will
be returned. More generally, ""-type <Substring of interesting type>[]"".

The start/end parameters can be obtained from the output of 'eeheap -gc'. For 
example, if you only want to list objects in the large heap segment:

    {prompt}eeheap -gc
    Number of GC Heaps: 1
    generation 0 starts at 0x00c32754
    generation 1 starts at 0x00c32748
    generation 2 starts at 0x00a71000
     segment    begin allocated     size
    00a70000 00a71000  010443a8 005d33a8(6108072)
    Large object heap starts at 0x01a71000
     segment    begin allocated     size
    01a70000 01a71000  01a75000 0x00004000(16384)
    Total Size  0x5d73a8(6124456)
    ------------------------------
    GC Heap Size  0x5d73a8(6124456)

    {prompt}dumpheap 1a71000 1a75000
     Address       MT     Size
    01a71000 5ba88bd8     2064
    01a71810 0019fe48     2032 Free
    01a72000 5ba88bd8     4096
    01a73000 0019fe48     4096 Free
    01a74000 5ba88bd8     4096
    total 5 objects
    Statistics:
          MT    Count TotalSize Class Name
    0019fe48        2      6128      Free
    5ba88bd8        3     10256 System.Object[]
    Total 5 objects

Finally, if GC heap corruption is present, you may see an error like this:

    {prompt}dumpheap -stat
    object 00a73d24: does not have valid MT
    curr_object : 00a73d24
    Last good object: 00a73d14
    ----------------

That indicates a serious problem. See the help for 'verifyheap' for more 
information on diagnosing the cause.
";
        private void ParseArguments()
        {
            if (!Runtime.Heap.CanWalkHeap && !IgnoreGCState)
            {
                throw new DiagnosticsException("The GC heap is not in a valid state for traversal.  (Use -ignoreGCState to override.)");
            }

            if (Live && Dead)
            {
                Live = false;
                Dead = false;
            }

            if (!string.IsNullOrWhiteSpace(MethodTableString))
            {
                if (TryParseAddress(MethodTableString, out ulong mt))
                {
                    MethodTable = mt;
                }
                else
                {
                    throw new ArgumentException($"Invalid MethodTable: {MethodTableString}");
                }
            }

            FilteredHeap = new(Runtime.Heap);
            if (GCHeap >= 0)
            {
                FilteredHeap.GCHeap = GCHeap;
            }

            if (TryParseAddress(Segment, out ulong segment))
            {
                FilteredHeap.FilterBySegmentHex(segment);
            }
            else if (!string.IsNullOrWhiteSpace(Segment))
            {
                throw new DiagnosticsException($"Failed to parse segment '{Segment}'.");
            }

            if (MemoryRange is not null && MemoryRange.Length > 0)
            {
                if (MemoryRange.Length > 2)
                {
                    string badArgument = MemoryRange.FirstOrDefault(f => f.StartsWith("-") || f.StartsWith("/"));
                    if (badArgument != null)
                    {
                        throw new ArgumentException($"Unknown argument: {badArgument}");
                    }

                    throw new ArgumentException("Too many arguments to !dumpheap");
                }

                string start = MemoryRange[0];
                string end = MemoryRange.Length > 1 ? MemoryRange[1] : null;
                FilteredHeap.FilterByHexMemoryRange(start, end);
            }

            if (Min > 0)
            {
                FilteredHeap.MinimumObjectSize = Min;
            }

            if (Max > 0)
            {
                FilteredHeap.MaximumObjectSize = Max;
            }

            if (Strings)
            {
                MethodTable = Runtime.Heap.StringType.MethodTable;
            }

            if (!string.IsNullOrWhiteSpace(Generation))
            {
                Generation generation = Generation.ToLowerInvariant() switch
                {
                    "gen0" => Diagnostics.Runtime.Generation.Generation0,
                    "gen1" => Diagnostics.Runtime.Generation.Generation1,
                    "gen2" => Diagnostics.Runtime.Generation.Generation2,
                    "loh" or "large" => Diagnostics.Runtime.Generation.Large,
                    "poh" or "pinned" => Diagnostics.Runtime.Generation.Pinned,
                    "foh" or "frozen" => Diagnostics.Runtime.Generation.Frozen,
                    _ => throw new ArgumentException($"Unknown generation: {Generation}. Only gen0, gen1, gen2, loh (large), poh (pinned) and foh (frozen) are supported")
                };

                FilteredHeap.Generation = generation;
            }

            FilteredHeap.SortSegments = (seg) => seg.OrderBy(seg => seg.Start);
        }
    }
}
