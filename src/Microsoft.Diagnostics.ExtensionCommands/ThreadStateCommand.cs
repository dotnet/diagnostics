// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting.Interop;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "threadstate", Aliases = new[] { "ThreadState" }, Help = "Decodes a thread state bitmask.")]
    public sealed class ThreadStateCommand(IServiceProvider services) : CommandBase(services)
    {
        private static readonly string[] s_flagNames =
        [
            "Thread Abort Requested",
            "GC Suspend Pending",
            "User Suspend Pending",
            "Debug Suspend Pending",
            "GC On Transitions",
            "Legal to Join",
            "Yield Requested",
            "Hijacked by the GC",
            "Blocking GC for Stack Overflow",
            "Background",
            "Unstarted",
            "Dead",
            "CLR Owns",
            "CoInitialized",
            "In Single Threaded Apartment",
            "In Multi Threaded Apartment",
            "Reported Dead",
            "Fully initialized",
            "Task Reset",
            "Sync Suspended",
            "Debug Will Sync",
            "Stack Crawl Needed",
            "Suspend Unstarted",
            "Aborted",
            "Thread Pool Worker Thread",
            "Interruptible",
            "Interrupted",
            "Completion Port Thread",
            "Abort Initiated",
            "Finalized",
            "Failed to Start",
            "Detached",
        ];

        [Argument<string[]>(Name = "state", Help = "Thread state in hexadecimal or a debugger expression.")]
        public string[] State { get; set; }

        public override void Invoke()
        {
            string expression = State is null ? "" : string.Join(" ", State);
            INativeDebuggerExpressionService evaluator = Services.GetService<INativeDebuggerExpressionService>();
            // Unlike address arguments, native threadstate honors the debugger's current expression radix.
            bool parsed = evaluator is not null && expression.Length != 0
                ? evaluator.EvaluateExpression(expression, out ulong value) == 0
                : TryParseAddress(expression, out value);
            uint state = unchecked((uint)value);
            if (!parsed || state == 0)
            {
                WriteLine("    No thread states for '{0}'", expression);
                return;
            }

            for (int bit = 0; bit < s_flagNames.Length; bit++)
            {
                if ((state & (1u << bit)) != 0)
                {
                    WriteLine("    {0}", s_flagNames[bit]);
                }
            }
        }
    }
}
