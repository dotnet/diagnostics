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
    [Command(Name = "registers", Help = "Displays the thread's registers.")]
    public class RegistersCommand : CommandBase
    {
        [Argument(Help = "The thread index to display, otherwise use the current thread.")]
        public int? ThreadIndex { get; set; } = null;

        public DataTarget DataTarget { get; set; }

        public AnalyzeContext AnalyzeContext { get; set; }

        public RegisterService RegisterService { get; set; }

        public override void Invoke()
        {
            IEnumerable<uint> threads = DataTarget.DataReader.EnumerateAllThreads();
            uint threadId;

            if (ThreadIndex.HasValue)
            {
                if (ThreadIndex.Value >= threads.Count()) {
                    throw new InvalidOperationException($"Invalid thread index {ThreadIndex.Value}");
                }
                threadId = threads.ElementAt(ThreadIndex.Value);
            }
            else
            {
                threadId = (uint)AnalyzeContext.CurrentThreadId;
            }

            foreach (RegisterService.RegisterInfo register in RegisterService.Registers)
            {
                if (RegisterService.GetRegisterValue(threadId, register.RegisterIndex, out ulong value))
                {
                    switch (register.RegisterSize)
                    {
                        case 1:
                            WriteLine("{0} = {1:X1}", register.RegisterName, value);
                            break;
                        case 2:
                            WriteLine("{0} = {1:X4}", register.RegisterName, value);
                            break;
                        case 4:
                            WriteLine("{0} = {1:X8}", register.RegisterName, value);
                            break;
                        case 8:
                            WriteLine("{0} = {1:X16}", register.RegisterName, value);
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
