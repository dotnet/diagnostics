using System;
using System.Buffers;
using System.IO;
using System.Linq;
using Graphs;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class PrintReportHelper
    {
        public static void WriteToStdOut(this MemoryGraph memoryGraph)
        {
            var allocations = ArrayPool<(int Index, Graph.TypeInfo Type)>.Shared.Rent(memoryGraph.m_types.Count);
            try
            {
                var histogramByType = memoryGraph.GetHistogramByType();
                var allocationSize = 0;
                var count = 0;

                foreach (var type in memoryGraph.m_types)
                {
                    allocations[count++] = (count - 1, type);
                    allocationSize += Math.Abs(type.Size);
                }

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
                Console.Out.Write($"  {"Count",15:N0}");
                Console.Out.Write("  Type");
                Console.WriteLine();


                var filteredTypes = allocations
                    .Take(count)
                    .Where(t => !string.IsNullOrEmpty(t.Type.Name) && t.Type.Size > 0)
                    .OrderByDescending(t => t.Type.Size);
                foreach (var type in filteredTypes)
                {
                    WriteFixedWidth(type.Type.Size);
                    Console.Out.Write("  ");
                    var s = histogramByType.FirstOrDefault(c => (int) c.TypeIdx == type.Index);
                    var node = memoryGraph.GetNode((NodeIndex) type.Index, memoryGraph.AllocNodeStorage());
                    if (s != null)
                    {
                        WriteFixedWidth(s.Count);
                        Console.Out.Write("  ");
                    }
                    else
                    {
                        Console.Out.Write($"{"",15}  ");
                    }
                    
                    Console.Out.Write(type.Type.Name ?? "<null>");
                    var dllName = GetDllName(type.Type.ModuleName ?? "");
                    if (!dllName.IsEmpty)
                    {
                        Console.Out.Write("  ");
                        Console.Out.Write('[');
                        Console.Out.Write(GetDllName(type.Type.ModuleName ?? ""));
                        Console.Out.Write(']');
                    }

                    Console.Out.WriteLine();
                }
            }
            finally
            {
                ArrayPool<(int Index, Graph.TypeInfo Type)>.Shared.Return(allocations);
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
    }
}