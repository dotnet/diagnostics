// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System;

namespace Microsoft.Diagnostics.Repl
{
    [Command(Name = "help", Help = "Displays help for a command.", Flags = CommandFlags.Global | CommandFlags.Manual)]
    public class HelpCommand : CommandBase
    {
        [Argument(Help = "Command to find help.")]
        public string Command { get; set; }

        private readonly ICommandService _commandService;
        private readonly IServiceProvider _services;

        public HelpCommand(ICommandService commandService, IServiceProvider services)
        {
            _commandService = commandService;
            _services = services;
        }

        public override void Invoke()
        {
            if (!_commandService.DisplayHelp(Command, _services))
            {
                throw new NotSupportedException($"Help for {Command} not found");
            }
        }
    }
}
