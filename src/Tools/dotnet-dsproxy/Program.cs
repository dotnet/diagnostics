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
        delegate Task<int> DiagnosticServerIpcClientTcpServerProxyDelegate(CancellationToken ct, string ipcClient, string tcpServer, bool autoShutdown, bool debug);
        delegate Task<int> DiagnosticServerIpcServerTcpServerProxyDelegate(CancellationToken ct, string ipcServer, string tcpServer, bool autoShutdown, bool debug);

        private static Command IpcClientTcpServerProxyCommand() =>
            new Command(
                name: "client-server",
                description:    @"Start a .NET application Diagnostic Server proxying local IPC server <--> remote TCP client.
                                Proxy is configured using an IPC client (connecting diagnostic tool IPC server)
                                and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticServerIpcClientTcpServerProxyDelegate)new DiagnosticServerProxyCommands().RunIpcClientTcpServerProxy).GetCommandHandler(),
                // Options
                IpcClientAddressOption(), TcpServerAddressOption(), AutoShutdownOption(), DebugOption()
            };

        private static Command IpcServerTcpServerProxyCommand() =>
            new Command(
                name: "server-server",
                description:    @"Start a .NET application Diagnostic Server proxying local IPC client <--> remote TCP client.
                                Proxy is configured using an IPC server (connecting to by diagnostic tools)
                                and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticServerIpcClientTcpServerProxyDelegate)new DiagnosticServerProxyCommands().RunIpcServerTcpServerProxy).GetCommandHandler(),
                // Options
                IpcServerAddressOption(), TcpServerAddressOption(), AutoShutdownOption(), DebugOption()
            };

        private static Option IpcClientAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-client", "-ipc-client" },
                description:    @"The diagnostic tool diagnostic server ipc address (--diagnostic-port argument).
                                Proxy connects diagnostic tool ipc server when establishing a
                                new proxy channel between runtime and diagnostic tool.")
            {
                Argument = new Argument<string>(name: "ipcClient", getDefaultValue: () => "")
            };

        private static Option IpcServerAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-server", "-ipc-server" },
                description:    @"The diagnostic server ipc address to proxy. Proxy accept ipc connections from diagnostic tools
                                establishing a new proxy channel between runtime and diagnostic tool. If not specified
                                proxy server will use default ipc diagnostic server path.")
            {
                Argument = new Argument<string>(name: "ipcServer", getDefaultValue: () => "")
            };

        private static Option TcpServerAddressOption() =>
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
                .AddCommand(IpcClientTcpServerProxyCommand())
                .AddCommand(IpcServerTcpServerProxyCommand())
                .UseDefaults()
                .Build();

            ParseResult parseResult = parser.Parse(args);
            return parser.InvokeAsync(args).Result;
        }
    }
}
