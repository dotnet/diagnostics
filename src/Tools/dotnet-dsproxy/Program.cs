// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        delegate Task<int> DiagnosticServerICTSProxyDelegate(CancellationToken ct, string ipcClient, string tcpServer, bool autoShutdown, bool debug);

        private static Command ClientServerICTSProxyCommand() =>
            new Command(
                name: "client-server",
                description:    @"Start a .NET application Diagnostic Server proxying local IPC server <--> remote TCP client.
                                Proxy is configured using an IPC client (connecting diagnostic tool IPC server)
                                and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticServerICTSProxyDelegate)new DiagnosticServerProxy().RunICTSProxy).GetCommandHandler(),
                // Options
                ClientAddressOption(), ServerAddressOption(), AutoShutdownOption(), DebugOption()
            };

        private static Option ClientAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-client", "-ipc-client" },
                description:    @"The diagnostic tool diagnostic server IPC address (--diagnostic-port argument).
                                Proxy connects diagnostic tool IPC server when establishing a
                                new proxy channel between runtime and diagnostic tool.")
            {
                Argument = new Argument<string>(name: "ipcClient", getDefaultValue: () => "")
            };

        private static Option ServerAddressOption() =>
            new Option(
                aliases: new[] { "--tcp-server", "-tcp-server" },
                description:    @"The proxy server TCP/IP address using format [host]:[port].
                                Proxy server can bind one (127.0.0.1, [::1], 0.0.0.0, [::], ipv4 address, ipv6 address, hostname)
                                or all (*) interfaces. Launch runtime using DOTNET_DiagnosticPorts environment variable
                                connecting proxy TCP server during startup.")
            {
                Argument = new Argument<string>(name: "tcpServer", getDefaultValue: () => "")
            };

        private static Option AutoShutdownOption() =>
            new Option(
                aliases: new[] { "--auto-shutdown", "-auto-shutdown" },
                description:    @"Automatically shutdown proxy server if no runtime connects to it before timeout.")
            {
                Argument = new Argument<bool>(name: "autoShutdown", getDefaultValue: () => true)
            };

        private static Option DebugOption() =>
            new Option(
                aliases: new[] { "--debug", "-debug" },
                description: @"Enable verbose logging.")
            {
                Argument = new Argument<bool>(name: "debug", getDefaultValue: () => false)
            };

        private static int Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(ClientServerICTSProxyCommand())
                .UseDefaults()
                .Build();

            ParseResult parseResult = parser.Parse(args);
            return parser.InvokeAsync(args).Result;
        }
    }
}
