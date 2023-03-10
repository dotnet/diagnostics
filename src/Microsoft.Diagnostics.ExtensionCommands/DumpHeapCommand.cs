using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    // TODO: thinlock, don't complete PR without it
    [Command(Name = "dumpheap", Help = "Displays a list of all managed objects.")]
    public class DumpHeapCommand : CommandBase
    {
        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [Option(Name = "--mt")]
        public string MethodTableString { get; set; }

        private ulong? MethodTable { get; set; }

        [Option(Name = "--type")]
        public string Type { get; set; }

        [Option(Name = "--stat")]
        public bool Stat { get; set; }

        [Option(Name = "--strings")]
        public bool Strings { get; set; }

        [Option(Name = "--verify")]
        public bool Verify { get; set; }

        [Option(Name = "--short")]
        public bool Short { get; set; }

        [Option(Name = "--min")]
        public ulong Min { get; set; }

        [Option(Name = "--max")]
        public ulong Max { get; set; }

        [Option(Name = "--live")]
        public bool Live { get; set; }

        [Option(Name = "--dead")]
        public bool Dead{ get; set; }

        [Option(Name = "--gcheap", Aliases = new string[] { "-h" })]
        public int GCHeap { get; set; } = -1;

        [Option(Name = "--segment", Aliases = new string[] { "-s" })]
        public string Segment { get; set; }

        [Argument(Help = "Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        private HeapWithFilters FilteredHeap { get; set; }

        public override void Invoke()
        {
            Stopwatch sw = Stopwatch.StartNew();
            ParseArguments();

            TableOutput objectTable = new(Console, (12, "x12"), (12, "x12"), (12, ""), (0, ""));
            if (!Stat && !Short)
                objectTable.WriteRow("Address", "MT", "Size");

            bool checkTypeName = !string.IsNullOrWhiteSpace(Type);
            Dictionary<ulong, (int Count, ulong Size, string TypeName)> stats = new();

            foreach (ClrObject obj in FilteredHeap.EnumerateFilteredObjects(Console.CancellationToken))
            {
                ulong mt = obj.Type?.MethodTable ?? 0;
                if (mt == 0)
                    MemoryService.ReadPointer(obj, out mt);

                // Filter by MT
                if (MethodTable.HasValue && mt != MethodTable.Value)
                    continue;

                // Filter by liveness
                if (Live && !LiveObjects.IsLive(obj))
                    continue;

                if (Dead && LiveObjects.IsLive(obj))
                    continue;
                    
                // Filter by type name
                if (checkTypeName)
                {
                    string typeName = obj.Type?.Name ?? "";
                    if (!typeName.Contains(Type))
                        continue;
                }

                if (Short)
                {
                    Console.WriteLine(obj.Address.ToString("x12"));
                    continue;
                }

                ulong size = obj.Size;
                if (!Stat)
                    objectTable.WriteRow(new DmlDumpObj(obj), new DmlDumpHeapMT(obj.Type?.MethodTable ?? 0), size, obj.IsFree ? "Free" : "");

                if (!stats.TryGetValue(mt, out var typeStats))
                    stats.Add(mt, (1, size, obj.Type?.Name ?? $"<unknown_type_{mt:x}>"));
                else
                    stats[mt] = (typeStats.Count + 1, typeStats.Size + size, typeStats.TypeName);
            }

            if (!Short)
            {
                if (stats.Any())
                {
                    if (!Stat)
                       Console.WriteLine();

                    Console.WriteLine("Statistics:");
                    int countLen = stats.Values.Max(ts => ts.Count).ToString("n0").Length;
                    countLen = Math.Max(countLen, "Count".Length);

                    int sizeLen = stats.Values.Max(ts => ts.Size).ToString("n0").Length;
                    sizeLen = Math.Max(countLen, "TotalSize".Length);

                    TableOutput statsTable = new(Console, (12, "x12"), (countLen, "n0"), (sizeLen, "n0"), (0, ""));
                    statsTable.WriteRow("MT", "Count", "TotalSize", "Class Name");

                    var statsSorted = from item in stats
                                      let MethodTable = item.Key
                                      let Size = item.Value.Size
                                      orderby Size
                                      select new
                                      {
                                          MethodTable = item.Key,
                                          item.Value.Count,
                                          Size,
                                          item.Value.TypeName
                                      };

                    foreach (var item in statsSorted)
                        statsTable.WriteRow(item.MethodTable, item.Count, item.Size, item.TypeName);

                    Console.WriteLine($"Total {stats.Values.Sum(r => r.Count):n0} objects");
                }
            }

            TimeSpan elapsed = sw.Elapsed;
            Console.WriteLine($"Elapsed: {elapsed} ({elapsed.Milliseconds:n0})");
        }

        private void ParseArguments()
        {
            if (Live && Dead)
            {
                Live = false;
                Dead = false;
            }

            if (!string.IsNullOrWhiteSpace(MethodTableString))
            {
                if (ParseHexString(MethodTableString, out ulong mt))
                    MethodTable = mt;
                else
                    throw new ArgumentException($"Invalid MethodTable: {MethodTableString}");
            }

            FilteredHeap = new(Runtime.Heap);
            if (GCHeap >= 0)
                FilteredHeap.GCHeap = GCHeap;

            if (!string.IsNullOrWhiteSpace(Segment))
                FilteredHeap.FilterBySegmentHex(Segment);

            if (MemoryRange is not null && MemoryRange.Length > 0)
            {
                if (MemoryRange.Length > 2)
                {
                    string badArgument = MemoryRange.FirstOrDefault(f => f.StartsWith("-") || f.StartsWith("/"));
                    if (badArgument != null)
                        throw new ArgumentException($"Unknown argument: {badArgument}");

                    throw new ArgumentException("Too many arguments to !dumpheap");
                }

                string start = MemoryRange[0];
                string end = MemoryRange.Length > 1 ? MemoryRange[1] : null;
                FilteredHeap.FilterByHexMemoryRange(start, end);
            }

            if (Min > 0)
                FilteredHeap.MinimumObjectSize = Min;

            if (Max > 0)
                FilteredHeap.MaximumObjectSize = Max;

            FilteredHeap.SortSegments = (seg) => seg.OrderBy(seg => seg.Start);
        }

        private bool ParseHexString(string str, out ulong value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(str))
                return false;

            if (!ulong.TryParse(str, NumberStyles.HexNumber, null, out value))
            {
                if (str.StartsWith("/") || str.StartsWith("-"))
                    throw new ArgumentException($"Unknown argument: {str}");

                throw new ArgumentException($"Unknown format: {str}, expected hex number");
            }

            return true;
        }
    }
}
