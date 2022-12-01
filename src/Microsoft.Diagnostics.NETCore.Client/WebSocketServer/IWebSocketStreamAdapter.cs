// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.NETCore.Client.WebSocketServer;

// The streams returned by IWebSocketServer implement the usual .NET Stream class, but they also
// expose a way to check if the underlying websocket connection is still open.
internal interface IWebSocketStreamAdapter
{
    public bool IsConnected { get; }
}