// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client;

internal sealed class IpcWebSocketServerTransport : IpcServerTransport
{
    public IpcWebSocketServerTransport(string address, int maxAllowedConnections, IIpcServerTransportCallbackInternal transportCallback = null)
        : base(transportCallback)
    {
    }

    protected override void Dispose(bool disposing)
    {
    }

    public override async Task<Stream> AcceptAsync(CancellationToken token)
    {
        WebSocketServer.IWebSocketServer server = WebSocketServer.WebSocketServerProvider.GetWebSocketServerInstance();
        return await server.AcceptConnection(token);
    }
}
