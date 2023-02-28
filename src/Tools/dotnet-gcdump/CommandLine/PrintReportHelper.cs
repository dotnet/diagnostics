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

            var filteredTypes = GetReportItem(memoryGraph)
                .OrderByDescending(t => t.SizeBytes)
                .ThenByDescending(t => t.Count);

            foreach (var filteredType in filteredTypes)
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
                var dllName = GetDllName(filteredType.ModuleName ?? "");
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
            var histogramByType = memoryGraph.GetHistogramByType();
            for (var index = 0; index < memoryGraph.m_types.Count; index++)
            {
                var type = memoryGraph.m_types[index];
                if (string.IsNullOrEmpty(type.Name) || type.Size == 0)
                {
                    continue;
                }

                var sizeAndCount = histogramByType.FirstOrDefault(c => (int) c.TypeIdx == index);
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
