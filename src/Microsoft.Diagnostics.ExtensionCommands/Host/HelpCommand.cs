// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "help", Help = "Displays help for a command.", Flags = CommandFlags.Global)]
    public class HelpCommand : CommandBase
    {
        [Argument(Help = "Command to find help.")]
        public string Command { get; set; }

        [ServiceImport]
        public ICommandService CommandService { get; set; }

        [ServiceImport]
        public IServiceProvider Services { get; set; }

        public override void Invoke()
        {
            if (!CommandService.DisplayHelp(Command, Services))
            {
                throw new NotSupportedException($"Help for {Command} not found");
            }
        }
    }
}
