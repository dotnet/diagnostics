// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Repl;
using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "help", Help = "Display help for a command.")]
    [CommandAlias(Name = "soshelp")]
    public class HelpCommand : CommandBase
    {
        [Argument(Help = "Command to find help.")]
        public string Command { get; set; }

        public CommandProcessor CommandProcessor { get; set; }

        public IHelpBuilder HelpBuilder { get; set; }

        public override void Invoke()
        {
            Command command = CommandProcessor.GetCommand(Command);
            if (command != null) {
                HelpBuilder.Write(command);
            }
            else {
                WriteLineError($"Help for {Command} not found.");
            }
        }
    }
}
