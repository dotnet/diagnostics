// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal abstract class IpcEndpoint
    {
        /// <summary>
        /// Connects to the underlying IPC transport and opens a read/write-able Stream
        /// </summary>
        /// <param name="timeout">The amount of time to block attempting to connect</param>
        /// <returns>A stream used for writing and reading data to and from the target .NET process</returns>
        public abstract Stream Connect(TimeSpan timeout);

        /// <summary>
        /// Connects to the underlying IPC transport and opens a read/write-able Stream
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task that completes with a stream used for writing and reading data to and from the target .NET process.
        /// </returns>
        public abstract Task<Stream> ConnectAsync(CancellationToken token);

        /// <summary>
        /// Wait for an available diagnostic endpoint to the runtime instance.
        /// </summary>
        /// <param name="timeout">The amount of time to wait before cancelling the wait for the connection.</param>
        public abstract void WaitForConnection(TimeSpan timeout);

        /// <summary>
        /// Wait for an available diagnostic endpoint to the runtime instance.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task that completes when a diagnostic endpoint to the runtime instance becomes available.
        /// </returns>
        public abstract Task WaitForConnectionAsync(CancellationToken token);
    }

    internal class ServerIpcEndpoint : IpcEndpoint
    {
        private readonly Guid _runtimeId;
        private readonly ReversedDiagnosticsServer _server;

        public ServerIpcEndpoint(ReversedDiagnosticsServer server, Guid runtimeId)
        {
            _runtimeId = runtimeId;
            _server = server;
        }

        /// <remarks>
        /// This will block until the diagnostic stream is provided. This block can happen if
        /// the stream is acquired previously and the runtime instance has not yet reconnected
        /// to the reversed diagnostics server.
        /// </remarks>
        public override Stream Connect(TimeSpan timeout)
        {
            return _server.Connect(_runtimeId, timeout);
        }

        public override Task<Stream> ConnectAsync(CancellationToken token)
        {
            return _server.ConnectAsync(_runtimeId, token);
        }

        public override void WaitForConnection(TimeSpan timeout)
        {
            _server.WaitForConnection(_runtimeId, timeout);
        }

        public override Task WaitForConnectionAsync(CancellationToken token)
        {
            return _server.WaitForConnectionAsync(_runtimeId, token);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ServerIpcEndpoint);
        }

        public bool Equals(ServerIpcEndpoint other)
        {
            return other != null && other._runtimeId == _runtimeId && other._server == _server;
        }

        public override int GetHashCode()
        {
            return _runtimeId.GetHashCode() ^ _server.GetHashCode();
        }
    }

    internal class PidIpcEndpoint : IpcEndpoint
    {
        public static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();
        public static string DiagnosticsPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnet-diagnostic-(\d+)$" : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

        private int _pid;

        /// <summary>
        /// Creates a reference to a .NET process's IPC Transport
        /// using the default rules for a given pid
        /// </summary>
        /// <param name="pid">The pid of the target process</param>
        /// <returns>A reference to the IPC Transport</returns>
        public PidIpcEndpoint(int pid)
        {
            _pid = pid;
        }

        public override Stream Connect(TimeSpan timeout)
        {
            string address = GetDefaultAddress();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var namedPipe = new NamedPipeClientStream(
                    ".",
                    address,
                    PipeDirection.InOut,
                    PipeOptions.None,
                    TokenImpersonationLevel.Impersonation);
                namedPipe.Connect((int)timeout.TotalMilliseconds);
                return namedPipe;
            }
            else
            {
                var socket = new IpcUnixDomainSocket();
                socket.Connect(new IpcUnixDomainSocketEndPoint(Path.Combine(IpcRootPath, address)), timeout);
                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
        }

        public override async Task<Stream> ConnectAsync(CancellationToken token)
        {
            string address = GetDefaultAddress();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var namedPipe = new NamedPipeClientStream(
                    ".",
                    address,
                    PipeDirection.InOut,
                    PipeOptions.None,
                    TokenImpersonationLevel.Impersonation);
                await namedPipe.ConnectAsync(token).ConfigureAwait(false);
                return namedPipe;
            }
            else
            {
                var socket = new IpcUnixDomainSocket();
                await socket.ConnectAsync(new IpcUnixDomainSocketEndPoint(Path.Combine(IpcRootPath, address)), token).ConfigureAwait(false);
                return new ExposedSocketNetworkStream(socket, ownsSocket: true);
            }
        }

        public override void WaitForConnection(TimeSpan timeout)
        {
            using var _ = Connect(timeout);
        }

        public override async Task WaitForConnectionAsync(CancellationToken token)
        {
            using var _ = await ConnectAsync(token).ConfigureAwait(false);
        }

        private string GetDefaultAddress()
        {
            try
            {
                var process = Process.GetProcessById(_pid);
            }
            catch (ArgumentException)
            {
                throw new ServerNotAvailableException($"Process {_pid} is not running.");
            }
            catch (InvalidOperationException)
            {
                throw new ServerNotAvailableException($"Process {_pid} seems to be elevated.");
            }

            if (!TryGetDefaultAddress(_pid, out string transportName))
            {
                throw new ServerNotAvailableException($"Process {_pid} not running compatible .NET runtime.");
            }

            return transportName;
        }

        private static bool TryGetDefaultAddress(int pid, out string defaultAddress)
        {
            defaultAddress = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (File.Exists($"{IpcRootPath}dotnet-dsrouter-diagnostic-{pid}"))
                    defaultAddress = $"dotnet-dsrouter-diagnostic-{pid}";
                else
                    defaultAddress = $"dotnet-diagnostic-{pid}";
            }
            else
            {
                try
                {
                    defaultAddress = Directory.GetFiles(IpcRootPath, $"dotnet-dsrouter-diagnostic-{pid}-*-socket") // Try best match.
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .FirstOrDefault();

                    if (string.IsNullOrEmpty(defaultAddress))
                        defaultAddress = Directory.GetFiles(IpcRootPath, $"dotnet-diagnostic-{pid}-*-socket") // Try best match.
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .FirstOrDefault();
                }
                catch (InvalidOperationException)
                {
                }
            }

            return !string.IsNullOrEmpty(defaultAddress);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PidIpcEndpoint);
        }

        public bool Equals(PidIpcEndpoint other)
        {
            return other != null && other._pid == _pid;
        }

        public override int GetHashCode()
        {
            return _pid.GetHashCode();
        }
    }
}