// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.Repl
{
    [Command(Name = "exit", Aliases = new string[] { "quit", "q" }, Help = "Exits interactive mode.", Flags = CommandFlags.Global | CommandFlags.Manual)]
    public class ExitCommand : CommandBase
    {
        private readonly Action _exit;

        public ExitCommand(Action exit)
        {
            _exit = exit;
        }

        public override void Invoke()
        {
            _exit();
        }
    }
}
