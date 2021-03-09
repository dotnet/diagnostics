// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.DSProxy
{

    internal class Program
    {
        delegate Task<int> DiagnosticServerProxyDelegate(CancellationToken ct, string clientAddress, string serverAddress);

        private static Command ClientServerProxyCommand() =>
            new Command(
                name: "client-server",
                description: "Start a .NET application Diagnostic Server proxying local IPC client <--> remote IPC client.")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticServerProxyDelegate)new DiagnosticServerProxy().Run).GetCommandHandler(),
                // Options
                ClientAddressOption(), ServerAddressOption(),
            };

        private static Option ClientAddressOption() =>
            new Option(
                alias: "--client",
                description: "The tool diagnostic client address to connect.")
            {
                Argument = new Argument<string>(name: "clientAddress")
            };

        private static Option ServerAddressOption() =>
            new Option(
                alias: "--server",
                description: "The proxy diagnostic server TCP/IP address to bind.")
            {
                Argument = new Argument<string>(name: "serverAddress")
            };

        private static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(ClientServerProxyCommand())
                .UseDefaults()
                .Build();

            ParseResult parseResult = parser.Parse(args);
            return parser.InvokeAsync(args);
        }
    }
}
