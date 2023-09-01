// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using SOS.Hosting;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "sos", Aliases = new string[] { "ext" }, Help = "Executes various SOS debugging commands.", Flags = CommandFlags.Global | CommandFlags.Manual)]
    public class SOSCommand : CommandBase
    {
        private readonly CommandService _commandService;
        private readonly IServiceProvider _services;
        private SOSHost _sosHost;

        [Argument(Name = "arguments", Help = "SOS command and arguments.")]
        public string[] Arguments { get; set; }

        public SOSCommand(CommandService commandService, IServiceProvider services)
        {
            _commandService = commandService;
            _services = services;
        }

        public override void Invoke()
        {
            string commandLine;
            string commandName;
            if (Arguments != null && Arguments.Length > 0)
            {
                commandLine = string.Concat(Arguments.Select((arg) => arg + " ")).Trim();
                commandName = Arguments[0];
            }
            else
            {
                commandLine = commandName = "help";
            }
            if (_commandService.IsCommand(commandName))
            {
                try
                {
                    _commandService.Execute(commandLine, _services);
                    return;
                }
                catch (CommandNotSupportedException)
                {
                }
            }
            if (_sosHost is null)
            {
                _sosHost = _services.GetService<SOSHost>();
                if (_sosHost is null)
                {
                    throw new DiagnosticsException($"'{commandName}' command not found");
                }
            }
            _sosHost.ExecuteCommand(commandLine);
        }
    }
}
