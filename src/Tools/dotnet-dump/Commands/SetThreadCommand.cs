
using Microsoft.Diagnostic.Repl;
using System.CommandLine;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    [Command(Name = "setthread", Help = "Sets or displays the current thread id for the SOS commands.")]
    [CommandAlias(Name = "threads")]
    public class SetThreadCommand : CommandBase
    {
        [Argument(Help = "The thread id to set, otherwise displays the current id.")]
        public int? ThreadId { get; set; } = null;

        public AnalyzeContext AnalyzeContext { get; set; }

        public override Task InvokeAsync()
        {
            if (ThreadId.HasValue)
            {
                AnalyzeContext.CurrentThreadId = ThreadId.Value;
            }
            else
            {
                int index = 0;
                foreach (uint threadId in AnalyzeContext.Target.DataReader.EnumerateAllThreads())
                {
                    WriteLine("{0}{1} 0x{2:X4} ({2})", threadId == AnalyzeContext.CurrentThreadId ? "*" : " ", index, threadId);
                    index++;
                }
            }
            return Task.CompletedTask;
        }
    }
}
