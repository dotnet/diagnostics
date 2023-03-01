// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "threads", Aliases = new string[] { "setthread" }, Help = "Displays threads or sets the current thread.")]
    public class ThreadsCommand : CommandBase
    {
        [Argument(Help = "The thread index or id to set, otherwise displays the list of threads.")]
        public uint? Thread { get; set; } = null;

        [Option(Name = "--tid", Aliases = new string[] { "-t" }, Help = "<thread> is an OS thread id.")]
        public bool ThreadId { get; set; }

        [Option(Name = "--verbose", Aliases = new string[] { "-v" }, Help = "Displays more details.")]
        public bool Verbose { get; set; }

        [ServiceImport(Optional = true)]
        public IThread CurrentThread { get; set; }

        [ServiceImport]
        public IThreadService ThreadService { get; set; }

        [ServiceImport]
        public IContextService ContextService { get; set; }

        public override void Invoke()
        {
            if (Thread.HasValue)
            {
                IThread thread;
                if (ThreadId)
                {
                    thread = ThreadService.GetThreadFromId(Thread.Value);
                }
                else
                {
                    thread = ThreadService.GetThreadFromIndex(unchecked((int)Thread.Value));
                }
                ContextService.SetCurrentThread(thread.ThreadId);
            }
            else
            {
                uint currentThreadId = CurrentThread != null ? CurrentThread.ThreadId : uint.MaxValue;
                foreach (IThread thread in ThreadService.EnumerateThreads())
                {
                    WriteLine("{0}{1} 0x{2:X4} ({2})", thread.ThreadId == currentThreadId ? "*" : " ", thread.ThreadIndex, thread.ThreadId);
                    if (Verbose)
                    {
                        thread.TryGetRegisterValue(ThreadService.InstructionPointerIndex, out ulong ip);
                        thread.TryGetRegisterValue(ThreadService.StackPointerIndex, out ulong sp);
                        thread.TryGetRegisterValue(ThreadService.FramePointerIndex, out ulong fp);
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
