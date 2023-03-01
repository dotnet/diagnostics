// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Graphs;

namespace Microsoft.Diagnostics.Tools.GCDump.CommandLine
{
    internal static class PrintReportHelper
    {
        internal static void WriteToStdOut(this MemoryGraph memoryGraph)
        {
            // Print summary
            WriteSummaryRow(memoryGraph.TotalSize, "GC Heap bytes");
            WriteSummaryRow(memoryGraph.NodeCount, "GC Heap objects");

            if (memoryGraph.TotalNumberOfReferences > 0)
            {
                WriteSummaryRow(memoryGraph.TotalNumberOfReferences, "Total references");
            }

            Console.WriteLine();

            // Print Details
            Console.Out.Write($"{"Object Bytes",15:N0}");
            Console.Out.Write($"  {"Count",8:N0}");
            Console.Out.Write("  Type");
            Console.WriteLine();

            IOrderedEnumerable<ReportItem> filteredTypes = GetReportItem(memoryGraph)
                .OrderByDescending(t => t.SizeBytes)
                .ThenByDescending(t => t.Count);

            foreach (ReportItem filteredType in filteredTypes)
            {
                WriteFixedWidth(filteredType.SizeBytes);
                Console.Out.Write("  ");
                if (filteredType.Count.HasValue)
                {
                    Console.Out.Write($"{filteredType.Count.Value,8:N0}");
                    Console.Out.Write("  ");
                }
                else
                {
                    Console.Out.Write($"{"",8}  ");
                }

                Console.Out.Write(filteredType.TypeName ?? "<UNKNOWN>");
                ReadOnlySpan<char> dllName = GetDllName(filteredType.ModuleName ?? "");
                if (!dllName.IsEmpty)
                {
                    Console.Out.Write("  ");
                    Console.Out.Write('[');
                    Console.Out.Write(GetDllName(filteredType.ModuleName ?? ""));
                    Console.Out.Write(']');
                }

                Console.Out.WriteLine();
            }

            static ReadOnlySpan<char> GetDllName(ReadOnlySpan<char> input)
                => input.Slice(input.LastIndexOf(Path.DirectorySeparatorChar) + 1);

            static void WriteFixedWidth(long value)
                => Console.Out.Write($"{value,15:N0}");

            static void WriteSummaryRow(object value, string text)
            {
                Console.Out.Write($"{value,15:N0}  ");
                Console.Out.Write(text);
                Console.Out.WriteLine();
            }
        }

        private struct ReportItem
        {
            public int? Count { get; set; }
            public long SizeBytes { get; set; }
            public string TypeName { get; set; }
            public string ModuleName { get; set; }
        }

        private static IEnumerable<ReportItem> GetReportItem(MemoryGraph memoryGraph)
        {
            Graph.SizeAndCount[] histogramByType = memoryGraph.GetHistogramByType();
            for (int index = 0; index < memoryGraph.m_types.Count; index++)
            {
                Graph.TypeInfo type = memoryGraph.m_types[index];
                if (string.IsNullOrEmpty(type.Name) || type.Size == 0)
                {
                    continue;
                }

                Graph.SizeAndCount sizeAndCount = histogramByType.FirstOrDefault(c => (int)c.TypeIdx == index);
                if (sizeAndCount == null || sizeAndCount.Count == 0)
                {
                    continue;
                }

                yield return new ReportItem
                {
                    TypeName = type.Name,
                    ModuleName = type.ModuleName,
                    SizeBytes = type.Size,
                    Count = sizeAndCount.Count
                };
            }
        }
    }
}
