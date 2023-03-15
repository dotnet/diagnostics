// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "help", Aliases = new string[] { "soshelp" }, Help = "Displays help for a command.")]
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
            if (string.IsNullOrWhiteSpace(Command))
            {
                IEnumerable<(string Invocation, string Help)> commands = CommandService.GetHelp(Services);
                int invocationWidth = commands.Max((item) => item.Invocation.Length) + 4;

                Write(string.Concat(commands.
                     OrderBy(item => item.Invocation, StringComparer.OrdinalIgnoreCase).
                     Select((item) => $"{FormatInvocation(item.Invocation)}{item.Help}{Environment.NewLine}")));

                string FormatInvocation(string invocation) => invocation + new string(' ', invocationWidth - invocation.Length);
            }
            else
            {
                string helpText = CommandService.GetDetailedHelp(Command, Services, Console.WindowWidth) ?? throw new DiagnosticsException($"Help for {Command} not found");
                Write(helpText);
            }
        }
    }
}
