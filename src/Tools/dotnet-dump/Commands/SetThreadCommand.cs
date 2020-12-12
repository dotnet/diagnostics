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
        public uint? Thread { get; set; } = null;

        [Option(Name = "--tid", Help = "<thread> is an OS thread id.")]
        [OptionAlias(Name = "-t")]
        public bool ThreadId { get; set; }

        [Option(Name = "--verbose", Help = "Displays more details.")]
        [OptionAlias(Name = "-v")]
        public bool Verbose { get; set; }

        public IThreadService ThreadService { get; set; }

        public override void Invoke()
        {
           if (Thread.HasValue)
            {
                IThread thread;
                if (ThreadId)
                {
                    thread = ThreadService.GetThreadInfoFromId(Thread.Value);
                }
                else
                {
                    thread = ThreadService.GetThreadInfoFromIndex(unchecked((int)Thread.Value));
                }
                ThreadService.CurrentThreadId = thread.ThreadId;
            }
            else
            {
                uint currentThreadId = ThreadService.CurrentThreadId.GetValueOrDefault(uint.MaxValue);
                foreach (IThread thread in ThreadService.EnumerateThreads())
                {
                    WriteLine("{0}{1} 0x{2:X4} ({2})", thread.ThreadId == currentThreadId ? "*" : " ", thread.ThreadIndex, thread.ThreadId);
                    if (Verbose)
                    {
                        thread.GetRegisterValue(ThreadService.InstructionPointerIndex, out ulong ip);
                        thread.GetRegisterValue(ThreadService.StackPointerIndex, out ulong sp);
                        thread.GetRegisterValue(ThreadService.FramePointerIndex, out ulong fp);
                        WriteLine("    IP  0x{0:X16}", ip);
                        WriteLine("    SP  0x{0:X16}", sp);
                        WriteLine("    FP  0x{0:X16}", fp);
                        WriteLine("    TEB 0x{0:X16}", thread.GetThreadTeb());
                    }
                }
            }
        }
    }
}
