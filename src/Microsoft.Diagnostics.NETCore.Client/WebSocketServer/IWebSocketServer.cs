// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.NETCore.Client.WebSocketServer;

// This interface abstracts the web socket server implementation used by dotnet-dsrouter
// in order to avoid a dependency on ASP.NET in the client library.
internal interface IWebSocketServer
{
    public Task<Stream> AcceptConnection(CancellationToken cancellationToken);
}