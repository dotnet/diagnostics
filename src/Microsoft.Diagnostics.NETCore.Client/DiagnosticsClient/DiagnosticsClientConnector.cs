// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// A connector that allows to create a <see cref="DiagnosticsClient"/> from a diagnostic port.
    /// </summary>
    public sealed class DiagnosticsClientConnector : IAsyncDisposable
    {
        private bool _disposed;
        private readonly IAsyncDisposable _server;

        internal DiagnosticsClientConnector(DiagnosticsClient diagnosticClient, IAsyncDisposable server)
        {
            _server = server;
            Instance = diagnosticClient;
        }

        /// <summary>
        /// Gets the <see cref="DiagnosticsClient"/> instance.
        /// </summary>
        public DiagnosticsClient Instance { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (_server != null)
            {
                await _server.DisposeAsync().ConfigureAwait(false);
            }

            _disposed = true;
        }

        /// <summary>
        /// Create a new <see cref="DiagnosticsClientConnector"/> instance using the specified diagnostic port.
        /// </summary>
        /// <param name="diagnosticPort">The diagnostic port.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="DiagnosticsClientConnector"/> instance</returns>
        public static async Task<DiagnosticsClientConnector> FromDiagnosticPort(string diagnosticPort, CancellationToken ct)
        {
            if (diagnosticPort is null)
            {
                throw new ArgumentNullException(nameof(diagnosticPort));
            }

            IpcEndpointConfig portConfig = IpcEndpointConfig.Parse(diagnosticPort);

            if (portConfig.IsListenConfig)
            {
                string fullPort = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? portConfig.Address : Path.GetFullPath(portConfig.Address);
                ReversedDiagnosticsServer server = new(fullPort);
                server.Start();

                try
                {
                    IpcEndpointInfo endpointInfo = await server.AcceptAsync(ct).ConfigureAwait(false);
                    return new DiagnosticsClientConnector(new DiagnosticsClient(endpointInfo.Endpoint), server);
                }
                catch (TaskCanceledException)
                {
                    //clean up the server
                    await server.DisposeAsync().ConfigureAwait(false);
                    if (!ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    return null;
                }
            }

            Debug.Assert(portConfig.IsConnectConfig);
            return new DiagnosticsClientConnector(new DiagnosticsClient(portConfig), null);
        }
    }
}
