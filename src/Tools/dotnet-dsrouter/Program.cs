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
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    internal class Program
    {
        private delegate Task<int> DiagnosticsServerIpcClientTcpServerRouterDelegate(CancellationToken ct, string ipcClient, string tcpServer, int runtimeTimeoutS, string verbose, string forwardPort);

        private delegate Task<int> DiagnosticsServerIpcServerTcpServerRouterDelegate(CancellationToken ct, string ipcServer, string tcpServer, int runtimeTimeoutS, string verbose, string forwardPort);

        private delegate Task<int> DiagnosticsServerIpcServerTcpClientRouterDelegate(CancellationToken ct, string ipcServer, string tcpClient, int runtimeTimeoutS, string verbose, string forwardPort);

        private delegate Task<int> DiagnosticsServerIpcClientTcpClientRouterDelegate(CancellationToken ct, string ipcClient, string tcpClient, int runtimeTimeoutS, string verbose, string forwardPort);

        private delegate Task<int> DiagnosticsServerIpcServerWebSocketServerRouterDelegate(CancellationToken ct, string ipcServer, string webSocket, int runtimeTimeoutS, string verbose);

        private delegate Task<int> DiagnosticsServerIpcClientWebSocketServerRouterDelegate(CancellationToken ct, string ipcClient, string webSocket, int runtimeTimeoutS, string verbose);

        private static Command IpcClientTcpServerRouterCommand() =>
            new Command(
                name: "client-server",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> remote TCP client. " +
                                "Router is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticsServerIpcClientTcpServerRouterDelegate)new DiagnosticsServerRouterCommands().RunIpcClientTcpServerRouter).GetCommandHandler(),
                // Options
                IpcClientAddressOption(), TcpServerAddressOption(), RuntimeTimeoutOption(), VerboseOption(), ForwardPortOption()
            };

        private static Command IpcServerTcpServerRouterCommand() =>
            new Command(
                name: "server-server",
                description: "Start a .NET application Diagnostics Server routing local IPC client <--> remote TCP client. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticsServerIpcServerTcpServerRouterDelegate)new DiagnosticsServerRouterCommands().RunIpcServerTcpServerRouter).GetCommandHandler(),
                // Options
                IpcServerAddressOption(), TcpServerAddressOption(), RuntimeTimeoutOption(), VerboseOption(), ForwardPortOption()
            };

        private static Command IpcServerTcpClientRouterCommand() =>
            new Command(
                name: "server-client",
                description: "Start a .NET application Diagnostics Server routing local IPC client <--> remote TCP server. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP client (connecting runtime TCP server).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticsServerIpcServerTcpClientRouterDelegate)new DiagnosticsServerRouterCommands().RunIpcServerTcpClientRouter).GetCommandHandler(),
                // Options
                IpcServerAddressOption(), TcpClientAddressOption(), RuntimeTimeoutOption(), VerboseOption(), ForwardPortOption()
            };

        private static Command IpcServerWebSocketServerRouterCommand() =>
        new Command(
            name: "server-websocket",
            description: "Starts a .NET application Diagnostic Server routing local IPC client <--> remote WebSocket client. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a WebSocket server (accepting runtime WebSocket client).")
        {
            HandlerDescriptor.FromDelegate((DiagnosticsServerIpcServerWebSocketServerRouterDelegate)new DiagnosticsServerRouterCommands().RunIpcServerWebSocketServerRouter).GetCommandHandler(),
            // Options
            IpcServerAddressOption(), WebSocketURLAddressOption(), RuntimeTimeoutOption(), VerboseOption()
        };

        private static Command IpcClientWebSocketServerRouterCommand() =>
        new Command(
            name: "client-websocket",
            description: "Starts a .NET application Diagnostic Server routing local IPC server <--> remote WebSocket client. " +
                                "Router is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                "and a WebSocket server (accepting runtime WebSocket client).")
        {
            // Handler
            HandlerDescriptor.FromDelegate((DiagnosticsServerIpcClientWebSocketServerRouterDelegate)new DiagnosticsServerRouterCommands().RunIpcClientWebSocketServerRouter).GetCommandHandler(),
            // Options
            IpcClientAddressOption(), WebSocketURLAddressOption(), RuntimeTimeoutOption(), VerboseOption()
        };

        private static Command IpcClientTcpClientRouterCommand() =>
            new Command(
                name: "client-client",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> remote TCP server. " +
                                "Router is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                "and a TCP/IP client (connecting runtime TCP server).")
            {
                // Handler
                HandlerDescriptor.FromDelegate((DiagnosticsServerIpcServerTcpClientRouterDelegate)new DiagnosticsServerRouterCommands().RunIpcClientTcpClientRouter).GetCommandHandler(),
                // Options
                IpcClientAddressOption(), TcpClientAddressOption(), RuntimeTimeoutOption(), VerboseOption(), ForwardPortOption()
            };

        private static Option IpcClientAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-client", "-ipcc" },
                description: "The diagnostic tool diagnostics server ipc address (--diagnostic-port argument). " +
                                "Router connects diagnostic tool ipc server when establishing a " +
                                "new route between runtime and diagnostic tool.")
            {
                Argument = new Argument<string>(name: "ipcClient", getDefaultValue: () => "")
            };

        private static Option IpcServerAddressOption() =>
            new Option(
                aliases: new[] { "--ipc-server", "-ipcs" },
                description: "The diagnostics server ipc address to route. Router accepts ipc connections from diagnostic tools " +
                                "establishing a new route between runtime and diagnostic tool. If not specified " +
                                "router will use default ipc diagnostics server path.")
            {
                Argument = new Argument<string>(name: "ipcServer", getDefaultValue: () => "")
            };

        private static Option TcpClientAddressOption() =>
            new Option(
                aliases: new[] { "--tcp-client", "-tcpc" },
                description: "The runtime TCP/IP address using format [host]:[port]. " +
                                "Router can can connect 127.0.0.1, [::1], ipv4 address, ipv6 address, hostname addresses." +
                                "Launch runtime using DOTNET_DiagnosticPorts environment variable to setup listener")
            {
                Argument = new Argument<string>(name: "tcpClient", getDefaultValue: () => "")
            };

        private static Option TcpServerAddressOption() =>
            new Option(
                aliases: new[] { "--tcp-server", "-tcps" },
                description: "The router TCP/IP address using format [host]:[port]. " +
                                "Router can bind one (127.0.0.1, [::1], 0.0.0.0, [::], ipv4 address, ipv6 address, hostname) " +
                                "or all (*) interfaces. Launch runtime using DOTNET_DiagnosticPorts environment variable " +
                                "connecting router TCP server during startup.")
            {
                Argument = new Argument<string>(name: "tcpServer", getDefaultValue: () => "")
            };

        private static Option WebSocketURLAddressOption() =>
            new Option(
                aliases: new[] { "--web-socket", "-ws" },
                description: "The router WebSocket address using format ws://[host]:[port]/[path] or wss://[host]:[port]/[path]. " +
                                "Launch app with WasmExtraConfig property specifying diagnostic_options with a server connect_url")
            {
                Argument = new Argument<string>(name: "webSocketURI", getDefaultValue: () => "")
            };

        private static Option RuntimeTimeoutOption() =>
            new Option(
                aliases: new[] { "--runtime-timeout", "-rt" },
                description: "Automatically shutdown router if no runtime connects to it before specified timeout (seconds)." +
                                "If not specified, router won't trigger an automatic shutdown.")
            {
                Argument = new Argument<int>(name: "runtimeTimeout", getDefaultValue: () => Timeout.Infinite)
            };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] { "--verbose", "-v" },
                description: "Enable verbose logging (debug|trace)")
            {
                Argument = new Argument<string>(name: "verbose", getDefaultValue: () => "")
            };

        private static Option ForwardPortOption() =>
            new Option(
                aliases: new[] { "--forward-port", "-fp" },
                description: "Enable port forwarding, values Android|iOS for TcpClient and only Android for TcpServer. Make sure to set ANDROID_SDK_ROOT before using this option on Android.")
            {
                Argument = new Argument<string>(name: "forwardPort", getDefaultValue: () => "")
            };

        private static int Main(string[] args)
        {
            StringBuilder message = new StringBuilder();

            var currentColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: dotnet-dsrouter is a development tool not intended for production environments." + Environment.NewLine);
            Console.ForegroundColor = currentColor;

            var parser = new CommandLineBuilder()
                .AddCommand(IpcClientTcpServerRouterCommand())
                .AddCommand(IpcServerTcpServerRouterCommand())
                .AddCommand(IpcServerTcpClientRouterCommand())
                .AddCommand(IpcClientTcpClientRouterCommand())
                .AddCommand(IpcServerWebSocketServerRouterCommand())
                .AddCommand(IpcClientWebSocketServerRouterCommand())
                .UseDefaults()
                .Build();

            ParseResult parseResult = parser.Parse(args);

            if (parseResult.UnparsedTokens.Count > 0)
            {
                ProcessLauncher.Launcher.PrepareChildProcess(args);
            }

            return parser.InvokeAsync(args).Result;
        }
    }
}
