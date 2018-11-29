using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class DumpHeapCommand : DumpCommandBase
    {
        public override IReadOnlyList<string> Names { get; } = new[] { "DumpHeap" };

        public override string Description => "Dumps objects from the .NET Heap";

        protected override Task RunAsyncCoreAsync(IConsole console, string[] args, AnalysisSession session, MemoryDump dump)
        {
            var stats = dump.ComputeHeapStatistics().OrderBy(s => s.TotalSize);
            console.WriteLine("              MT    Count    TotalSize Class Name");
            foreach (var heapStats in stats)
            {
                console.WriteLine($"{heapStats.Type.MethodTable:X16} {heapStats.Count.ToString().PadLeft(8)} {heapStats.TotalSize.ToString().PadLeft(12)} {heapStats.Type.Name}");
            }

            return Task.CompletedTask;
        }

        public override Task WriteHelpAsync(IConsole console)
        {
            console.WriteLine("TODO");
            return Task.CompletedTask;
        }
    }
}
