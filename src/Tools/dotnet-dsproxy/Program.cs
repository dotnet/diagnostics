// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Text;
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
        delegate Task<int> DiagnosticsServerIpcClientTcpServerProxyDelegate(CancellationToken ct, string ipcClient, string tcpServer, int runtimeTimeoutS, bool verbose);
        delegate Task<int> DiagnosticsServerIpcServerTcpServerProxyDelegate(CancellationToken ct, string ipcServer, string tcpServer, int runtimeTimeoutS, bool verbose);

        private static Command IpcClientTcpServerProxyCommand() =>
            new Command(
                name: "client-server",
                description:    "Start a .NET application Diagnostics Server proxying local IPC server <--> remote TCP client. " +
                                "Proxy is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticsServerIpcClientTcpServerProxyDelegate)new DiagnosticsServerProxyCommands().RunIpcClientTcpServerProxy).GetCommandHandler(),
                // Options
                IpcClientAddressOption(), TcpServerAddressOption(), RuntimeTimeoutOption(), VerboseOption()
            };

        private static Command IpcServerTcpServerProxyCommand() =>
            new Command(
                name: "server-server",
                description:    "Start a .NET application Diagnostics Server proxying local IPC client <--> remote TCP client. " +
                                "Proxy is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticsServerIpcClientTcpServerProxyDelegate)new DiagnosticsServerProxyCommands().RunIpcServerTcpServerProxy).GetCommandHandler(),
                // Options
                IpcServerAddressOption(), TcpServerAddressOption(), RuntimeTimeoutOption(), VerboseOption()
            };

        private static Option IpcClientAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-client", "-ipcc" },
                description:    "The diagnostic tool diagnostics server ipc address (--diagnostic-port argument). " +
                                "Proxy connects diagnostic tool ipc server when establishing a " +
                                "new proxy channel between runtime and diagnostic tool.")
            {
                Argument = new Argument<string>(name: "ipcClient", getDefaultValue: () => "")
            };

        private static Option IpcServerAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-server", "-ipcs" },
                description:    "The diagnostics server ipc address to proxy. Proxy accept ipc connections from diagnostic tools " +
                                "establishing a new proxy channel between runtime and diagnostic tool. If not specified " +
                                "proxy server will use default ipc diagnostics server path.")
            {
                Argument = new Argument<string>(name: "ipcServer", getDefaultValue: () => "")
            };

        private static Option TcpServerAddressOption() =>
            new Option(
                aliases: new[] { "--tcp-server", "-tcps" },
                description:    "The proxy server TCP/IP address using format [host]:[port]. " +
                                "Proxy server can bind one (127.0.0.1, [::1], 0.0.0.0, [::], ipv4 address, ipv6 address, hostname) " +
                                "or all (*) interfaces. Launch runtime using DOTNET_DiagnosticPorts environment variable " +
                                "connecting proxy TCP server during startup.")
            {
                Argument = new Argument<string>(name: "tcpServer", getDefaultValue: () => "")
            };

        private static Option RuntimeTimeoutOption() =>
            new Option(
                aliases: new[] { "--runtime-timeout", "-rt" },
                description:    "Automatically shutdown proxy server if no runtime connects to it before specified timeout (seconds)." +
                                "If not specified, proxy server won't trigger an automatic shutdown.")
            {
                Argument = new Argument<int>(name: "runtimeTimeout", getDefaultValue: () => Timeout.Infinite)
            };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] { "--verbose", "-v" },
                description:    "Enable verbose logging.")
            {
                Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
            };

        private static int Main(string[] args)
        {
            StringBuilder message = new StringBuilder();

            var currentColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: dotnet-dsproxy is an experimental development tool not intended for production environments." + Environment.NewLine);
            Console.ForegroundColor = currentColor;

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
