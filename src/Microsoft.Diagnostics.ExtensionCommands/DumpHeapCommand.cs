// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpheap", Help = "Displays a list of all managed objects.")]
    public class DumpHeapCommand : CommandBase
    {
        private const char StringReplacementCharacter = '.';

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [Option(Name = "-mt")]
        public string MethodTableString { get; set; }

        private ulong? MethodTable { get; set; }

        [Option(Name = "-type")]
        public string Type { get; set; }

        [Option(Name = "-stat")]
        public bool StatOnly { get; set; }

        [Option(Name = "-strings")]
        public bool Strings { get; set; }

        [Option(Name = "-verify")]
        public bool Verify { get; set; }

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

        [Argument(Help = "Optional memory ranges in the form of: [Start [End]]")]
        public string[] MemoryRange { get; set; }

        private HeapWithFilters FilteredHeap { get; set; }

        public override void Invoke()
        {
            ParseArguments();

            TableOutput thinLockOutput = null;
            TableOutput objectTable = new(Console, (12, "x12"), (12, "x12"), (12, ""), (0, ""));
            if (!StatOnly && !Short && !ThinLock)
            {
                objectTable.WriteRow("Address", "MT", "Size");
            }

            bool checkTypeName = !string.IsNullOrWhiteSpace(Type);
            Dictionary<ulong, (int Count, ulong Size, string TypeName)> stats = new();
            Dictionary<(string String, ulong Size), uint> stringTable = null;

            foreach (ClrObject obj in FilteredHeap.EnumerateFilteredObjects(Console.CancellationToken))
            {
                ulong mt = obj.Type?.MethodTable ?? 0;
                if (mt == 0)
                {
                    MemoryService.ReadPointer(obj, out mt);
                }

                // Filter by MT, if the user specified -strings then MethodTable has been pre-set
                // to the string MethodTable
                if (MethodTable.HasValue && mt != MethodTable.Value)
                {
                    continue;
                }

                // Filter by liveness
                if (Live && !LiveObjects.IsLive(obj))
                {
                    continue;
                }

                if (Dead && LiveObjects.IsLive(obj))
                {
                    continue;
                }

                // Filter by type name
                if (checkTypeName && obj.Type?.Name is not null && !obj.Type.Name.Contains(Type))
                {
                    continue;
                }

                if (ThinLock)
                {
                    ClrThinLock thinLock = obj.GetThinLock();
                    if (thinLock != null)
                    {
                        if (thinLockOutput is null)
                        {
                            thinLockOutput = new(Console, (12, "x"), (16, "x"), (16, "x"), (10, "n0"));
                            thinLockOutput.WriteRow("Object", "Thread", "OSId", "Recursion");
                        }

                        thinLockOutput.WriteRow(new DmlDumpObj(obj), thinLock.Thread?.Address ?? 0, thinLock.Thread?.OSThreadId ?? 0, thinLock.Recursion);
                    }

                    continue;
                }

                if (Short)
                {
                    Console.WriteLine(obj.Address.ToString("x12"));
                    continue;
                }

                ulong size = obj.IsValid ? obj.Size : 0;
                if (!StatOnly)
                {
                    objectTable.WriteRow(new DmlDumpObj(obj), new DmlDumpHeapMT(obj.Type?.MethodTable ?? 0), size, obj.IsFree ? "Free" : "");
                }

                if (Strings)
                {
                    // We only read a maximum of 1024 characters for each string.  This may lead to some collisions if strings are unique
                    // only after their 1024th character while being the exact same size as another string.  However, this will be correct
                    // the VAST majority of the time, and it will also keep us from hitting OOM or other weirdness if the heap is corrupt.

                    string value = obj.AsString(1024);

                    stringTable ??= new();
                    (string value, ulong size) key = (value, size);
                    stringTable.TryGetValue(key, out uint stringCount);
                    stringTable[key] = stringCount + 1;
                }
                else
                {
                    if (!stats.TryGetValue(mt, out (int Count, ulong Size, string TypeName) typeStats))
                    {
                        stats.Add(mt, (1, size, obj.Type?.Name ?? $"<unknown_type_{mt:x}>"));
                    }
                    else
                    {
                        stats[mt] = (typeStats.Count + 1, typeStats.Size + size, typeStats.TypeName);
                    }
                }
            }

            // Print statistics, but not for -short or -thinlock
            if (!Short && !ThinLock)
            {
                if (Strings && stringTable is not null)
                {
                    // For -strings, we print the strings themselves with their stats
                    if (!StatOnly)
                    {
                        Console.WriteLine();
                    }

                    int countLen = stringTable.Max(ts => ts.Value).ToString("n0").Length;
                    countLen = Math.Max(countLen, "Count".Length);

                    int sizeLen = stringTable.Max(ts => ts.Key.Size * ts.Value).ToString("n0").Length;
                    sizeLen = Math.Max(countLen, "TotalSize".Length);

                    int stringLen = 128;
                    int possibleWidth = Console.WindowWidth - countLen - sizeLen - 2;
                    if (possibleWidth > 16)
                    {
                        stringLen = Math.Min(possibleWidth, stringLen);
                    }

                    Console.WriteLine("Statistics:");
                    TableOutput statsTable = new(Console, (countLen, "n0"), (sizeLen, "n0"), (0, ""));

                    var stringsSorted = from item in stringTable
                                        let Count = item.Value
                                        let Size = item.Key.Size
                                        let String = Sanitize(item.Key.String, stringLen)
                                        let TotalSize = Count * Size
                                        orderby TotalSize
                                        select new
                                        {
                                            Count,
                                            TotalSize,
                                            String
                                        };

                    foreach (var item in stringsSorted)
                    {
                        statsTable.WriteRow(item.Count, item.TotalSize, item.String);
                    }
                }
                else if (stats.Count != 0)
                {
                    // Print statistics table
                    if (!StatOnly)
                    {
                        Console.WriteLine();
                    }

                    int countLen = stats.Values.Max(ts => ts.Count).ToString("n0").Length;
                    countLen = Math.Max(countLen, "Count".Length);

                    int sizeLen = stats.Values.Max(ts => ts.Size).ToString("n0").Length;
                    sizeLen = Math.Max(countLen, "TotalSize".Length);

                    TableOutput statsTable = new(Console, (12, "x12"), (countLen, "n0"), (sizeLen, "n0"), (0, ""));

                    Console.WriteLine("Statistics:");
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
                    {
                        statsTable.WriteRow(item.MethodTable, item.Count, item.Size, item.TypeName);
                    }

                    Console.WriteLine($"Total {stats.Values.Sum(r => r.Count):n0} objects");
                }
            }
        }

        private string Sanitize(string str, int maxLen)
        {
            foreach (char ch in str)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    return FilterString(str, maxLen);
                }
            }

            return str;

            static string FilterString(string str, int maxLen)
            {
                maxLen = Math.Min(str.Length, maxLen);
                Debug.Assert(maxLen <= 128);

                Span<char> buffer = stackalloc char[maxLen];
                ReadOnlySpan<char> value = str.AsSpan(0, buffer.Length);

                for (int i = 0; i < value.Length; ++i)
                {
                    char ch = value[i];
                    buffer[i] = char.IsLetterOrDigit(ch) || char.IsPunctuation(ch) || ch == ' ' ? ch : StringReplacementCharacter;
                }

                return buffer.ToString();
            }
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

            if (!string.IsNullOrWhiteSpace(Segment))
            {
                FilteredHeap.FilterBySegmentHex(Segment);
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

            FilteredHeap.SortSegments = (seg) => seg.OrderBy(seg => seg.Start);
        }

        private static bool ParseHexString(string str, out ulong value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(str))
            {
                return false;
            }

            if (!ulong.TryParse(str, NumberStyles.HexNumber, null, out value))
            {
                if (str.StartsWith("/") || str.StartsWith("-"))
                {
                    throw new ArgumentException($"Unknown argument: {str}");
                }

                throw new ArgumentException($"Unknown format: {str}, expected hex number");
            }

            return true;
        }
    }
}
