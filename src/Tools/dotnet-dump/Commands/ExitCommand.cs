
using Microsoft.Diagnostic.Repl;
using System.CommandLine;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    [Command(Name = "exit", Help = "Exit interactive mode.")]
    [CommandAlias(Name = "quit")]
    public class ExitCommand : CommandBase
    {
        public AnalyzeContext AnalyzeContext { get; set; }

        public override Task InvokeAsync()
        {
            AnalyzeContext.Exit();
            return Task.CompletedTask;
        }
    }
}
