using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.NETCore.Client.WebSocketServer;

// This interface allows dotnet-dsrouter to install a callback that will create IWebSocketServer instances.
// This is used to avoid a dependency on ASP.NET in the client library.
internal class WebSocketServerProvider
{
    internal static void SetProvider(Func<IWebSocketServer> provider)
    {
        _provider = provider;
    }

    internal static IWebSocketServer GetWebSocketServerInstance()
    {
        return _provider();
    }

    private static Func<IWebSocketServer> _provider;
}