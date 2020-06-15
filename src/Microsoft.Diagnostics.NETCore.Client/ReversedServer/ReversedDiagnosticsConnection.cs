// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// Represents a runtine instance connection to a reversed diagnostics server.
    /// </summary>
    /// <remarks>
    /// This object must be disposed when the connection to the target runtime instance is
    /// no longer needed. Disposing this object will release the tracking resources from the
    /// reversed server from which this connection was produced.
    /// </remarks>
    [DebuggerDisplay("PID={ProcessId}, Cookie={RuntimeInstanceCookie}")]
    internal class ReversedDiagnosticsConnection : IDisposable
    {
        private readonly IIpcEndpoint _endpoint;
        private readonly int _processId;
        private readonly Guid _runtimeInstanceCookie;
        private readonly ReversedDiagnosticsServer _server;

        private bool _disposed;

        internal ReversedDiagnosticsConnection(ReversedDiagnosticsServer server, IIpcEndpoint endpoint, int processId, Guid runtimeInstanceCookie)
        {
            _endpoint = endpoint;
            _processId = processId;
            _runtimeInstanceCookie = runtimeInstanceCookie;
            _server = server;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _server.RemoveConnection(RuntimeInstanceCookie);

                _disposed = true;
            }
        }

        private void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ReversedDiagnosticsConnection));
            }
        }

        /// <summary>
        /// An endpoint used to retrieve diagnostic information from the associated runtime instance.
        /// </summary>
        public IIpcEndpoint Endpoint
        {
            get
            {
                VerifyNotDisposed();
                return _endpoint;
            }
        }

        /// <summary>
        /// The identifier of the process that is unique within its process namespace.
        /// </summary>
        public int ProcessId
        {
            get
            {
                VerifyNotDisposed();
                return _processId;
            }
        }

        /// <summary>
        /// The unique identifier of the runtime instance.
        /// </summary>
        public Guid RuntimeInstanceCookie
        {
            get
            {
                VerifyNotDisposed();
                return _runtimeInstanceCookie;
            }
        }
    }
}
