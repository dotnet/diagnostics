using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.NETCore.Client.WebSocketServer;

// This interface allows dotnet-dsrouter to install a callback that will create IWebSocketServer instances.
// This is used to avoid a dependency on ASP.NET in the client library.
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