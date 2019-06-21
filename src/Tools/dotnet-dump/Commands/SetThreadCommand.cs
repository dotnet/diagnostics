
using Microsoft.Diagnostics.Repl;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "setthread", Help = "Sets or displays the current thread for the SOS commands.")]
    [CommandAlias(Name = "threads")]
    public class SetThreadCommand : CommandBase
    {
        [Argument(Help = "The thread index to set, otherwise displays the list of threads.")]
        public int? ThreadIndex { get; set; } = null;

        public AnalyzeContext AnalyzeContext { get; set; }

        public override void Invoke()
        {
            if (ThreadIndex.HasValue)
            {
                IEnumerable<uint> threads = AnalyzeContext.Target.DataReader.EnumerateAllThreads();
                if (ThreadIndex.Value >= threads.Count()) {
                    throw new InvalidOperationException($"Invalid thread index {ThreadIndex.Value}");
                }
                AnalyzeContext.CurrentThreadId = unchecked((int)threads.ElementAt(ThreadIndex.Value));
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
        }
    }
}
