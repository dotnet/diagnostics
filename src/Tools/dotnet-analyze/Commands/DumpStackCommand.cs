// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class DumpStackCommand : DumpCommandBase
    {
        public override IReadOnlyList<string> Names { get; } = new [] { "dumpstack" };

        public override string Description => "Dumps the managed stack trace for the current thread.";

        protected override async Task RunAsyncCoreAsync(IConsole console, string[] args, AnalysisSession session, MemoryDump dump)
        {
            foreach(var frame in dump.ActiveThread.EnumerateStackTrace())
            {
                var methodInfo = frame.Method == null ? "<Unknown Method>" : GenerateMethodInfo(frame.Method);
                await console.Out.WriteLineAsync($"{frame.StackPointer:x16} {frame.InstructionPointer:x16} {methodInfo}");
            }
        }

        private string GenerateMethodInfo(ClrMethod method)
        {
            return $"{method.Type.Name}.{method.Name}";
        }

        public override Task WriteHelpAsync(IConsole console)
        {
            console.WriteLine("TODO");
            return Task.CompletedTask;
        }
    }
}
