// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using System;
using System.CommandLine;
using System.CommandLine.Help;

namespace Microsoft.Diagnostics.Repl
{
    [Command(Name = "help", Help = "Display help for a command.", Platform = CommandPlatform.Global)]
    public class HelpCommand : CommandBase
    {
        [Argument(Help = "Command to find help.")]
        public string Command { get; set; }

        private readonly CommandProcessor _commandProcessor;
        private readonly IServiceProvider _services;

        public HelpCommand(CommandProcessor commandProcessor, IServiceProvider services)
        {
            _commandProcessor = commandProcessor;
            _services = services;
        }

        public override void Invoke()
        {
            if (!_commandProcessor.DisplayHelp(Command, _services))
            {
                throw new NotSupportedException($"Help for {Command} not found");
            }
        }
    }
}
