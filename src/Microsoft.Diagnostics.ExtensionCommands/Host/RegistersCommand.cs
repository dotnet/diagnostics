// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "registers", Aliases = new string[] { "r" }, Help = "Displays the thread's registers.")]
    public class RegistersCommand : CommandBase
    {
        public IThreadService ThreadService { get; set; }

        public IThread CurrentThread { get; set; }

        [Option(Name = "--verbose", Aliases = new string[] { "-v" }, Help = "Displays more details.")]
        public bool Verbose { get; set; }

        public override void Invoke()
        {
            IThread thread = CurrentThread;
            if (thread == null)
            {
                throw new InvalidOperationException($"No current thread");
            }
            foreach (RegisterInfo register in ThreadService.Registers)
            {
                if (Verbose)
                {
                    WriteLine("{0} Index = {1} Offset = {2} Size = {3}", register.RegisterName, register.RegisterIndex, register.RegisterOffset, register.RegisterSize);
                }
                if (thread.TryGetRegisterValue(register.RegisterIndex, out ulong value))
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
