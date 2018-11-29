using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class ThreadsCommand : DumpCommandBase
    {
        public override IReadOnlyList<string> Names { get; } = new List<string>() { "threads", "~"};

        public override string Description => "Lists threads in the current dump.";

        protected override async Task RunAsyncCoreAsync(IConsole console, string[] args, AnalysisSession session, MemoryDump dump)
        {
            foreach(var thread in dump.Runtime.Threads)
            {
                var isActive = dump.ActiveThreadId == thread.ManagedThreadId ? "." : " ";
                await console.Out.WriteLineAsync($"{isActive}{thread.ManagedThreadId.ToString().PadLeft(2)} Id: {Utils.FormatAddress(thread.OSThreadId)} Teb: {Utils.FormatAddress(thread.Teb)}");
            }
        }

        public override Task WriteHelpAsync(IConsole console)
        {
            console.WriteLine("TODO");
            return Task.CompletedTask;
        }
    }
}
