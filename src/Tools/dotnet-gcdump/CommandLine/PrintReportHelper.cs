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
            var allocations = ArrayPool<Graph.TypeInfo>.Shared.Rent(memoryGraph.m_types.Count);
            try
            {
                var allocationSize = 0;
                var count = 0;

                foreach (var type in memoryGraph.m_types)
                {
                    allocations[count++] = type;
                    allocationSize += Math.Abs(type.Size);
                }

                // Print summary
                WriteSummaryRow(memoryGraph.TotalSize, "(Dump size)");
                WriteSummaryRow(allocationSize, "(Total allocations)");
                if (memoryGraph.TotalNumberOfReferences > 0)
                {
                    WriteSummaryRow(memoryGraph.TotalNumberOfReferences, "(Total number of references)");
                }

                Console.WriteLine();

                // Print Details
                var filteredTypes = allocations
                    .Take(count)
                    .Where(t => !string.IsNullOrEmpty(t.Name))
                    .OrderByDescending(t => t.Size);
                foreach (var type in filteredTypes)
                {
                    WriteFixedWidth(type.Size);
                    Console.Out.Write("  ");
                    Console.Out.Write(type.Name ?? "<null>");
                    var dllName = GetDllName(type.ModuleName ?? "");
                    if (!dllName.IsEmpty)
                    {
                        Console.Out.Write("  ");
                        Console.Out.Write('[');
                        Console.Out.Write(GetDllName(type.ModuleName ?? ""));
                        Console.Out.Write(']');
                    }

                    Console.Out.WriteLine();
                }
            }
            finally
            {
                ArrayPool<Graph.TypeInfo>.Shared.Return(allocations);
            }

            static ReadOnlySpan<char> GetDllName(ReadOnlySpan<char> input)
                => input.Slice(input.LastIndexOf(Path.DirectorySeparatorChar) + 1);

            static void WriteFixedWidth(long value)
                => Console.Out.Write($"{value,10:N0}");

            static void WriteSummaryRow(object value, string text)
            {
                Console.Out.Write($"{value,10:N0}  ");
                Console.Out.Write(text);
                Console.Out.WriteLine();
            }
        }
    }
}