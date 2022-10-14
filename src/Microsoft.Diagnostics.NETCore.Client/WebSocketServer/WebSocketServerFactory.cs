using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.NETCore.Client.WebSocketServer;

internal class WebSocketServerFactory
{
    internal static void SetBuilder(Func<IWebSocketServer> builder)
    {
        _builder = builder;
    }

    internal static IWebSocketServer CreateWebSocketServer()
    {
        return _builder();
    }

    private static Func<IWebSocketServer> _builder;
}