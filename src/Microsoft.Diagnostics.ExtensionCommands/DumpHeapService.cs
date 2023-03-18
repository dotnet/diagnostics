// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// A class to print output similar in the same way !dumpheap does to share the
    /// printing implementation and format.
    /// </summary>
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class DumpHeapService
    {
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

        public void PrintHeap(IEnumerable<ClrObject> objects, DisplayKind displayKind, bool statsOnly)
        {
            TableOutput thinLockOutput = null;
            TableOutput objectTable = new(Console, (12, "x12"), (12, "x12"), (12, ""), (0, ""));
            if (!statsOnly && (displayKind is DisplayKind.Normal or DisplayKind.Strings))
            {
                objectTable.WriteRow("Address", "MT", "Size");
            }

            Dictionary<ulong, (int Count, ulong Size, string TypeName)> stats = new();
            Dictionary<(string String, ulong Size), uint> stringTable = null;

            foreach (ClrObject obj in objects)
            {
                if (displayKind == DisplayKind.ThinLock)
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

                if (displayKind == DisplayKind.Short)
                {
                    Console.WriteLine(obj.Address.ToString("x12"));
                    continue;
                }

                ulong size = obj.IsValid ? obj.Size : 0;
                if (!statsOnly)
                {
                    objectTable.WriteRow(new DmlDumpObj(obj), new DmlDumpHeap(obj.Type?.MethodTable ?? 0), size, obj.IsFree ? "Free" : "");
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
                    TableOutput statsTable = new(Console, (countLen, "n0"), (sizeLen, "n0"), (0, ""));

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
                        statsTable.WriteRow(item.Count, item.TotalSize, item.String);
                    }
                }
            }
            else if (displayKind == DisplayKind.Normal)
            {
                if (stats.Count != 0)
                {
                    // Print statistics table
                    if (!statsOnly)
                    {
                        Console.WriteLine();
                    }

                    int countLen = stats.Values.Max(ts => ts.Count).ToString("n0").Length;
                    countLen = Math.Max(countLen, "Count".Length);

                    int sizeLen = stats.Values.Max(ts => ts.Size).ToString("n0").Length;
                    sizeLen = Math.Max(sizeLen, "TotalSize".Length);

                    TableOutput statsTable = new(Console, (12, "x12"), (countLen, "n0"), (sizeLen, "n0"), (0, ""));

                    Console.WriteLine("Statistics:");
                    statsTable.WriteRow("MT", "Count", "TotalSize", "Class Name");

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
                        statsTable.WriteRow(new DmlDumpHeap(item.MethodTable), item.Count, item.Size, item.TypeName);
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
    }
}
