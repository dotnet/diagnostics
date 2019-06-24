// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public abstract class DumpCommandBase : IAnalysisCommand
    {
        public abstract IReadOnlyList<string> Names { get; }
        public abstract string Description { get; }

        public async Task RunAsync(IConsole console, string[] args, AnalysisSession session)
        {
            if (session.Dump == null)
            {
                await console.Error.WriteLineAsync("This command requires a memory dump!");
            }
            else
            {
                await RunAsyncCoreAsync(console, args, session, session.Dump);
            }
        }

        public abstract Task WriteHelpAsync(IConsole console);
        protected abstract Task RunAsyncCoreAsync(IConsole console, string[] args, AnalysisSession session, MemoryDump dump);
    }
}
