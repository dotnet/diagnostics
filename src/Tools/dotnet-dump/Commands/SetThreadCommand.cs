// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "setthread", Help = "Sets or displays the current thread for the SOS commands.")]
    [CommandAlias(Name = "threads")]
    public class SetThreadCommand : CommandBase
    {
        [Argument(Help = "The thread index or id to set, otherwise displays the list of threads.")]
        public int? Thread { get; set; } = null;

        [Option(Name = "--tid", Help = "<thread> is an OS thread id.")]
        [OptionAlias(Name = "-t")]
        public bool ThreadId { get; set; }

        public AnalyzeContext AnalyzeContext { get; set; }

        public IThreadService ThreadService { get; set; }

        public override void Invoke()
        {
            if (Thread.HasValue)
            {
                ThreadInfo threadInfo;
                if (ThreadId)
                {
                    threadInfo = ThreadService.GetThreadInfoFromId((uint)Thread.Value);
                }
                else
                {
                    threadInfo = ThreadService.GetThreadInfoFromIndex(Thread.Value);
                }
                AnalyzeContext.CurrentThreadId = threadInfo.ThreadId;
            }
            else
            {
                foreach (ThreadInfo thread in ThreadService.EnumerateThreads())
                {
                    WriteLine("{0}{1} 0x{2:X4} ({2})", thread.ThreadId == AnalyzeContext.CurrentThreadId.Value ? "*" : " ", thread.ThreadIndex, thread.ThreadId);
                }
            }
        }
    }
}
