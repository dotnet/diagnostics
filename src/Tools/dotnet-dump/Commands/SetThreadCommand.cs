// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
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

        public DataTarget DataTarget { get; set; }

        public AnalyzeContext AnalyzeContext { get; set; }

        public override void Invoke()
        {
            if (ThreadIndex.HasValue)
            {
                IEnumerable<uint> threads = DataTarget.DataReader.EnumerateAllThreads();
                if (ThreadIndex.Value >= threads.Count()) {
                    throw new InvalidOperationException($"Invalid thread index {ThreadIndex.Value}");
                }
                AnalyzeContext.CurrentThreadId = unchecked((int)threads.ElementAt(ThreadIndex.Value));
            }
            else
            {
                int index = 0;
                foreach (uint threadId in DataTarget.DataReader.EnumerateAllThreads())
                {
                    WriteLine("{0}{1} 0x{2:X4} ({2})", threadId == AnalyzeContext.CurrentThreadId ? "*" : " ", index, threadId);
                    index++;
                }
            }
        }
    }
}
