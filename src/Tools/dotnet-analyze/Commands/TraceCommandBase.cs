using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public abstract class TraceCommandBase : IAnalysisCommand
    {
        public abstract IReadOnlyList<string> Names { get; }
        public abstract string Description { get; }

        public async Task RunAsync(IConsole console, string[] args, AnalysisSession session)
        {
            if (session.Trace == null)
            {
                await console.Error.WriteLineAsync("This command requires an event trace!");
            }
            else
            {
                await RunAsyncCore(console, args, session, session.Trace);
            }
        }

        public abstract Task WriteHelpAsync(IConsole console);
        protected abstract Task RunAsyncCore(IConsole console, string[] args, AnalysisSession session, TraceLog trace);
    }
}
