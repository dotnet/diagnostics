// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// A class to print output similar in the same way !dumpheap does to share the
    /// printing implementation and format.
    /// </summary>
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class DumpHeapService
    {
        private const ulong FragmentationBlockMinSize = 512 * 1024;
        private const char StringReplacementCharacter = '.';

        [ServiceImport]
        public IConsoleService Console { get; set; }

        [ServiceImport]
        public IMemoryService Memory { get; set; }

        public enum DisplayKind
        {
            Normal,
            Short,
            ThinLock,
            Strings
        }

        public void PrintHeap(IEnumerable<ClrObject> objects, DisplayKind displayKind, bool statsOnly, bool printFragmentation)
        {
            List<(ClrObject Free, ClrObject Next)> fragmentation = null;
            Dictionary<(string String, ulong Size), uint> stringTable = null;
            Dictionary<ulong, (int Count, ulong Size, string TypeName)> stats = new();

            Table thinLockOutput = null;
            Table objectTable = null;

            ClrObject lastFreeObject = default;
            foreach (ClrObject obj in objects)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (displayKind == DisplayKind.ThinLock)
                {
                    ClrThinLock thinLock = obj.GetThinLock();
                    if (thinLock != null)
                    {
                        if (thinLockOutput is null)
                        {
                            thinLockOutput = new(Console, ColumnKind.DumpObj, ColumnKind.Pointer, ColumnKind.HexValue, ColumnKind.Integer);
                            thinLockOutput.WriteHeader("Object", "Thread", "OSId", "Recursion");
                        }

                        thinLockOutput.WriteRow(obj, thinLock.Thread?.Address ?? 0, thinLock.Thread?.OSThreadId ?? 0, thinLock.Recursion);
                    }

                    continue;
                }

                if (displayKind == DisplayKind.Short)
                {
                    Console.WriteLine(obj.Address.ToString("x12"));
                    continue;
                }

                ulong size = obj.IsValid ? obj.Size : 0;
                if (!statsOnly)
                {
                    if (objectTable is null)
                    {
                        objectTable = new(Console, ColumnKind.DumpObj, ColumnKind.DumpHeapMT, ColumnKind.ByteCount, ColumnKind.Text);
                        if (displayKind is DisplayKind.Normal or DisplayKind.Strings)
                        {
                            objectTable.WriteHeader("Address", "MT", "Size");
                        }
                    }

                    objectTable.WriteRow(obj, obj.Type, obj.IsValid ? size : null, obj.IsFree ? "Free" : "");
                }

                if (printFragmentation)
                {
                    if (lastFreeObject.IsFree && obj.IsValid && !obj.IsFree)
                    {
                        // Check to see if the previous object lands directly before this one.  We don't want
                        // to print fragmentation after changing segments, or after an allocation context.
                        if (lastFreeObject.Address + lastFreeObject.Size == obj.Address)
                        {
                            // Also, don't report fragmentation for Large/Pinned/Frozen segments.  This check
                            // is a little slow, so we do this last.
                            ClrSegment seg = obj.Type.Heap.GetSegmentByAddress(obj);
                            if (seg is not null && seg.Kind is not GCSegmentKind.Large or GCSegmentKind.Pinned or GCSegmentKind.Frozen)
                            {
                                fragmentation ??= new();
                                fragmentation.Add((lastFreeObject, obj));
                            }
                        }
                    }

                    if (obj.IsFree && size >= FragmentationBlockMinSize)
                    {
                        lastFreeObject = obj;
                    }
                    else
                    {
                        lastFreeObject = default;
                    }
                }

                if (displayKind == DisplayKind.Strings)
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
                    ulong mt;
                    if (obj.Type is not null)
                    {
                        mt = obj.Type.MethodTable;
                    }
                    else
                    {
                        Memory.ReadPointer(obj, out mt);
                    }

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

            // Print statistics for Normal and String output
            if (displayKind == DisplayKind.Strings)
            {
                if (stringTable is not null)
                {
                    // For -strings, we print the strings themselves with their stats
                    if (!statsOnly)
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
                    Table statsTable = new(Console, ColumnKind.Integer, ColumnKind.ByteCount, ColumnKind.Text);

                    var stringsSorted = from item in stringTable
                                        let Count = item.Value
                                        let Size = item.Key.Size
                                        let String = Sanitize(item.Key.String, stringLen)
                                        let TotalSize = Count * Size
                                        orderby TotalSize
                                        select new {
                                            Count,
                                            TotalSize,
                                            String
                                        };

                    foreach (var item in stringsSorted)
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        statsTable.WriteRow(item.Count, item.TotalSize, item.String);
                    }
                }
            }
            else if (displayKind == DisplayKind.Normal)
            {
                // Print statistics table
                if (stats.Count != 0)
                {
                    // Print statistics table
                    if (!statsOnly)
                    {
                        Console.WriteLine();
                    }

                    Column countColumn = ColumnKind.Integer;
                    countColumn = countColumn.GetAppropriateWidth(stats.Values.Select(ts => ts.Count));

                    Column sizeColumn = ColumnKind.ByteCount;
                    sizeColumn = sizeColumn.GetAppropriateWidth(stats.Values.Select(ts => ts.Size));

                    Table statsTable = new(Console, ColumnKind.DumpHeapMT, countColumn, sizeColumn, ColumnKind.TypeName);

                    Console.WriteLine("Statistics:");
                    statsTable.WriteHeader("MT", "Count", "TotalSize", "Class Name");

                    var statsSorted = from item in stats
                                      let MethodTable = item.Key
                                      let Size = item.Value.Size
                                      orderby Size
                                      select new {
                                          MethodTable = item.Key,
                                          item.Value.Count,
                                          Size,
                                          item.Value.TypeName
                                      };

                    foreach (var item in statsSorted)
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        statsTable.WriteRow(item.MethodTable, item.Count, item.Size, item.TypeName);
                    }

                    Console.WriteLine($"Total {stats.Values.Sum(r => r.Count):n0} objects, {stats.Values.Sum(r => (long)r.Size):n0} bytes");
                }
            }

            // Print fragmentation if we calculated it
            PrintFragmentation(fragmentation);
        }

        private void PrintFragmentation(List<(ClrObject Free, ClrObject Next)> fragmentation)
        {
            if (fragmentation is null || fragmentation.Count == 0)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Fragmented blocks larger than 0.5 MB:");

            Table output = new(Console, ColumnKind.ListNearObj, ColumnKind.ByteCount, ColumnKind.DumpObj, ColumnKind.TypeName);
            output.WriteHeader("Address", "Size", "Followed By");

            foreach ((ClrObject free, ClrObject next) in fragmentation)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                output.WriteRow(free.Address, free.Size, next.Address, next.Type);
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
    }
}
