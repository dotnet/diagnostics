// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.DiagnosticsServerRouter
{
    internal sealed class Program
    {
        private static Command IpcClientTcpServerRouterCommand()
        {
            Command command = new(
                name: "client-server",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> remote TCP client. " +
                                "Router is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                IpcClientAddressOption, TcpServerAddressOption, RuntimeTimeoutOption, VerboseOption, ForwardPortOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcClientTcpServerRouter(
                ct,
                ipcClient: parseResult.GetValue(IpcClientAddressOption) ?? "",
                tcpServer: parseResult.GetValue(TcpServerAddressOption) ?? "",
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                forwardPort: parseResult.GetValue(ForwardPortOption) ?? ""
            ));

            return command;
        }

        private static Command IpcServerTcpServerRouterCommand()
        {
            Command command = new(
                name: "server-server",
                description: "Start a .NET application Diagnostics Server routing local IPC client <--> remote TCP client. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                IpcServerAddressOption, TcpServerAddressOption, RuntimeTimeoutOption, VerboseOption, ForwardPortOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerTcpServerRouter(
                ct,
                ipcServer: parseResult.GetValue(IpcServerAddressOption) ?? "",
                tcpServer: parseResult.GetValue(TcpServerAddressOption) ?? "",
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                forwardPort: parseResult.GetValue(ForwardPortOption) ?? ""
            ));

            return command;
        }

        private static Command IpcServerTcpClientRouterCommand()
        {
            Command command = new(
                name: "server-client",
                description: "Start a .NET application Diagnostics Server routing local IPC client <--> remote TCP server. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP client (connecting runtime TCP server).")
            {
                IpcServerAddressOption, TcpClientAddressOption, RuntimeTimeoutOption, VerboseOption, ForwardPortOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerTcpClientRouter(
                ct,
                ipcServer: parseResult.GetValue(IpcServerAddressOption) ?? "",
                tcpClient: parseResult.GetValue(TcpClientAddressOption) ?? "",
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                forwardPort: parseResult.GetValue(ForwardPortOption) ?? ""
            ));

            return command;
        }

        private static Command IpcServerWebSocketServerRouterCommand()
        {
            Command command = new(
                name: "server-websocket",
                description: "Starts a .NET application Diagnostic Server routing local IPC client <--> remote WebSocket client. " +
                                    "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                    "and a WebSocket server (accepting runtime WebSocket client).")
            {
                IpcServerAddressOption, WebSocketURLAddressOption, RuntimeTimeoutOption, VerboseOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerWebSocketServerRouter(
                ct,
                ipcServer: parseResult.GetValue(IpcServerAddressOption) ?? "",
                webSocket: parseResult.GetValue(WebSocketURLAddressOption) ?? "",
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption)
            ));

            return command;
        }

        private static Command IpcClientWebSocketServerRouterCommand()
        {
            Command command = new(
                name: "client-websocket",
                description: "Starts a .NET application Diagnostic Server routing local IPC server <--> remote WebSocket client. " +
                                    "Router is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                    "and a WebSocket server (accepting runtime WebSocket client).")
            {
                IpcClientAddressOption, WebSocketURLAddressOption, RuntimeTimeoutOption, VerboseOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcClientWebSocketServerRouter(
                ct,
                ipcClient: parseResult.GetValue(IpcClientAddressOption) ?? "",
                webSocket: parseResult.GetValue(WebSocketURLAddressOption) ?? "",
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption)
            ));

            return command;
        }

        private static Command IpcClientTcpClientRouterCommand()
        {
            Command command = new(
                name: "client-client",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> remote TCP server. " +
                                "Router is configured using an IPC client (connecting diagnostic tool IPC server) " +
                                "and a TCP/IP client (connecting runtime TCP server).")
            {
                IpcClientAddressOption, TcpClientAddressOption, RuntimeTimeoutOption, VerboseOption, ForwardPortOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcClientTcpClientRouter(
                ct,
                ipcClient: parseResult.GetValue(IpcClientAddressOption) ?? "",
                tcpClient: parseResult.GetValue(TcpClientAddressOption) ?? "",
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                forwardPort: parseResult.GetValue(ForwardPortOption) ?? ""
            ));

            return command;
        }

        private static Command IOSSimulatorRouterCommand()
        {
            Command command = new(
                name: "ios-sim",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> iOS Simulator. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                RuntimeTimeoutOption, VerboseOption, InfoOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerIOSSimulatorRouter(
                ct,
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                info: parseResult.GetValue(InfoOption)
            ));

            return command;
        }

        private static Command IOSRouterCommand()
        {
            Command command = new(
                name: "ios",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> iOS Device over usbmux. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP client (connecting runtime TCP server over usbmux).")
            {
                RuntimeTimeoutOption, VerboseOption, InfoOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerIOSRouter(
                ct,
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                info: parseResult.GetValue(InfoOption)
            ));

            return command;
        }

        private static Command AndroidEmulatorRouterCommand()
        {
            Command command = new(
                name: "android-emu",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> Android Emulator. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                RuntimeTimeoutOption, VerboseOption, InfoOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerAndroidEmulatorRouter(
                ct,
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                info: parseResult.GetValue(InfoOption)));

            return command;
        }

        private static Command AndroidRouterCommand()
        {
            Command command = new(
                name: "android",
                description: "Start a .NET application Diagnostics Server routing local IPC server <--> Android Device. " +
                                "Router is configured using an IPC server (connecting to by diagnostic tools) " +
                                "and a TCP/IP server (accepting runtime TCP client).")
            {
                RuntimeTimeoutOption, VerboseOption, InfoOption
            };

            command.SetAction((parseResult, ct) => new DiagnosticsServerRouterCommands().RunIpcServerAndroidRouter(
                ct,
                runtimeTimeout: parseResult.GetValue(RuntimeTimeoutOption),
                verbose: parseResult.GetValue(VerboseOption),
                info: parseResult.GetValue(InfoOption)));

            return command;
        }

        private static readonly Option<string> IpcClientAddressOption =
            new("--ipc-client", "-ipcc")
            {
                Description = "The diagnostic tool diagnostics server ipc address (--diagnostic-port argument). " +
                                "Router connects diagnostic tool ipc server when establishing a " +
                                "new route between runtime and diagnostic tool."
            };

        private static readonly Option<string> IpcServerAddressOption =
            new("--ipc-server", "-ipcs")
            {
                Description = "The diagnostics server ipc address to route. Router accepts ipc connections from diagnostic tools " +
                                "establishing a new route between runtime and diagnostic tool. If not specified " +
                                "router will use default ipc diagnostics server path."
            };

        private static readonly Option<string> TcpClientAddressOption =
            new("--tcp-client", "-tcpc")
            {
                Description = "The runtime TCP/IP address using format [host]:[port]. " +
                                "Router can can connect 127.0.0.1, [::1], ipv4 address, ipv6 address, hostname addresses." +
                                "Launch runtime using DOTNET_DiagnosticPorts environment variable to setup listener."
            };

        private static Option<string> TcpServerAddressOption =
            new("--tcp-server", "-tcps")
            {
                Description = "The router TCP/IP address using format [host]:[port]. " +
                                "Router can bind one (127.0.0.1, [::1], 0.0.0.0, [::], ipv4 address, ipv6 address, hostname) " +
                                "or all (*) interfaces. Launch runtime using DOTNET_DiagnosticPorts environment variable " +
                                "connecting router TCP server during startup."
            };

        private static readonly Option<string> WebSocketURLAddressOption =
            new("--web-socket", "-ws")
            {
                Description = "The router WebSocket address using format ws://[host]:[port]/[path] or wss://[host]:[port]/[path]. " +
                                "Launch app with WasmExtraConfig property specifying diagnostic_options with a server connect_url"
            };

        private static readonly Option<int> RuntimeTimeoutOption =
            new("--runtime-timeout", "-rt")
            {
                Description = "Automatically shutdown router if no runtime connects to it before specified timeout (seconds)." +
                                "If not specified, router won't trigger an automatic shutdown.",
                DefaultValueFactory = _ => Timeout.Infinite,
            };

        private static readonly Option<string> VerboseOption =
            new("--verbose", "-v")
            {
                Description = "Enable verbose logging (none|critical|error|warning|info|debug|trace)",
                DefaultValueFactory = _ => "info",
            };

        private static readonly Option<string> ForwardPortOption =
            new("--forward-port", "-fp")
            {
                Description = "Enable port forwarding, values Android|iOS for TcpClient and only Android for TcpServer. Make sure to set ANDROID_SDK_ROOT before using this option on Android."
            };

        private static readonly Option<bool> InfoOption =
            new("--info", "-i")
            {
                Description = "Print info on how to use current dotnet-dsrouter instance with application and diagnostic tooling."
            };

        private static Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new()
            {
                IpcClientTcpServerRouterCommand(),
                IpcServerTcpServerRouterCommand(),
                IpcServerTcpClientRouterCommand(),
                IpcClientTcpClientRouterCommand(),
                IpcServerWebSocketServerRouterCommand(),
                IpcClientWebSocketServerRouterCommand(),
                IOSSimulatorRouterCommand(),
                IOSRouterCommand(),
                AndroidEmulatorRouterCommand(),
                AndroidRouterCommand()
            };

            ParseResult parseResult = rootCommand.Parse(args);

            if (parseResult.UnmatchedTokens.Count > 0)
            {
                ProcessLauncher.Launcher.PrepareChildProcess(args);
            }

            string verbose = parseResult.GetValue(VerboseOption);
            if (!string.Equals(verbose, "none", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleColor currentColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARNING: dotnet-dsrouter is a development tool not intended for production environments." + Environment.NewLine);
                Console.ForegroundColor = currentColor;
            }

            return parseResult.InvokeAsync();
        }
    }
}
