using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class InfoCommand : IAnalysisCommand
    {
        public IReadOnlyList<string> Names { get; } = new List<string>() { "info" };
        public string Description => "Displays information about the current analysis session";

        public Task RunAsync(IConsole console, string[] args, AnalysisSession session)
        {
            if(session.Dump != null)
            {
                console.WriteLine("Memory Dump:");
                console.WriteLine($"  CLR Version: {session.Dump.Runtime.ClrInfo.Version}");
                console.WriteLine($"  CLR Flavor: {session.Dump.Runtime.ClrInfo.Flavor}");
            }
            else
            {
                console.WriteLine("No Memory Dump Loaded.");
            }

            if(session.Trace != null)
            {
                console.WriteLine("Trace:");
                console.WriteLine($"  OS: {session.Trace.OSName} {session.Trace.OSVersion}");
            }
            else
            {
                console.WriteLine("No Trace Loaded.");
            }
            return Task.CompletedTask;
        }

        public Task WriteHelpAsync(IConsole console)
        {
            console.WriteLine("TODO");
            return Task.CompletedTask;
        }
    }
}
