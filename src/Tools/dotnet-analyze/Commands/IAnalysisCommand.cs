using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public interface IAnalysisCommand
    {
        IReadOnlyList<string> Names { get; }
        string Description { get; }

        Task RunAsync(IConsole console, string[] args, AnalysisSession session);
        Task WriteHelpAsync(IConsole console);
    }
}
