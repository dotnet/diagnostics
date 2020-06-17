// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "registers", Help = "Displays the thread's registers.")]
    [CommandAlias(Name = "r")]
    public class RegistersCommand : CommandBase
    {
        [Argument(Help = "The thread index to display, otherwise use the current thread.")]
        public int? ThreadIndex { get; set; } = null;

        public AnalyzeContext AnalyzeContext { get; set; }

        public IThreadService ThreadService { get; set; }

        public override void Invoke()
        {
            uint threadId;
            if (ThreadIndex.HasValue)
            {
                threadId = ThreadService.GetThreadInfoFromIndex(ThreadIndex.Value).ThreadId;
            }
            else
            {
                threadId = AnalyzeContext.CurrentThreadId.Value;
            }
            foreach (RegisterInfo register in ThreadService.Registers)
            {
                if (ThreadService.GetRegisterValue(threadId, register.RegisterIndex, out ulong value))
                {
                    switch (register.RegisterSize)
                    {
                        case 1:
                            WriteLine("{0} = 0x{1:X1}", register.RegisterName, value);
                            break;
                        case 2:
                            WriteLine("{0} = 0x{1:X4}", register.RegisterName, value);
                            break;
                        case 4:
                            WriteLine("{0} = 0x{1:X8}", register.RegisterName, value);
                            break;
                        case 8:
                            WriteLine("{0} = 0x{1:X16}", register.RegisterName, value);
                            break;
                    }
                }
                else
                {
                    WriteLine("{0} = ", register.RegisterName);
                }
            }
        }
    }
}
