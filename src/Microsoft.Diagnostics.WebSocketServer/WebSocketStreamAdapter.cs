// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client.WebSocketServer;

namespace Microsoft.Diagnostics.WebSocketServer;

internal sealed class WebSocketStreamAdapter : Stream, IWebSocketStreamAdapter
{
    private readonly WebSocket _webSocket;
    private readonly Action _onDispose;

    public WebSocket WebSocket { get => _webSocket; }
    public WebSocketStreamAdapter(WebSocket webSocket, Action onDispose)
    {
        _webSocket = webSocket;
        _onDispose = onDispose;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override void Flush()
    {
        throw new NotImplementedException();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override long Length => throw new NotImplementedException();

    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();

    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken).ConfigureAwait(false);
        return result.Count;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ValueWebSocketReceiveResult result = await _webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        return result.Count;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _webSocket.SendAsync(new ArraySegment<byte>(buffer, offset, count), WebSocketMessageType.Binary, true, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
    {
        return _webSocket.SendAsync(memory, WebSocketMessageType.Binary, true, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _onDispose();
            _webSocket.Dispose();
        }
    }

    bool IWebSocketStreamAdapter.IsConnected => _webSocket.State == WebSocketState.Open;
}
