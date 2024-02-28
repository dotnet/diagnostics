// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using SOS.Hosting;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "sos", Aliases = new string[] { "ext" }, Help = "Executes various SOS debugging commands.")]
    public class SOSCommand : CommandBase
    {
        [ServiceImport]
        public CommandService CommandService { get; set; }

        [ServiceImport(Optional = true)]
        public SOSHost SOSHost { get; set; }

        [Argument(Name = "command_and_arguments", Help = "SOS command and arguments.")]
        public string[] Arguments { get; set; }

        public SOSCommand()
        {
        }

        public override void Invoke()
        {
            string command;
            string arguments;
            if (Arguments != null && Arguments.Length > 0)
            {
                command = Arguments[0];
                arguments = string.Concat(Arguments.Skip(1).Select((arg) => arg + " ")).Trim();
            }
            else
            {
                command = "help";
                arguments = null;
            }
            if (CommandService.Execute(command, arguments, Services))
            {
                return;
            }
            if (SOSHost is null)
            {
                throw new CommandNotFoundException($"{CommandNotFoundException.NotFoundMessage} '{command}'");
            }
            SOSHost.ExecuteCommand(command, arguments);
        }
    }
}
