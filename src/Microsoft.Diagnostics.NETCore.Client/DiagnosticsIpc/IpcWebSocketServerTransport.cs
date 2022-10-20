// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client;

internal sealed class IpcWebSocketServerTransport : IpcServerTransport
{
    private static Singleton singleton = new Singleton();
    private readonly CancellationTokenSource _cancellation;
    private readonly int _maxConnections;
    private readonly Uri _endPoint;
    public IpcWebSocketServerTransport(string address, int maxAllowedConnections, IIpcServerTransportCallbackInternal transportCallback = null)
        : base(transportCallback)
    {
        _maxConnections = maxAllowedConnections;
        ParseWebSocketURL(address, out _endPoint);
        _cancellation = new CancellationTokenSource();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Cancel();

            singleton.DropRef();

            _cancellation.Dispose();
        }
    }

    public override async Task<Stream> AcceptAsync(CancellationToken token)
    {
        if (singleton.AddRef())
        {
            await singleton.StartServer(_endPoint, token);
        }
        Stream s = await singleton.AcceptConnection(token);
        return s;
    }

    private static void ParseWebSocketURL(string endPoint, out Uri uri)
    {
        string uriToParse;
        // Host can contain wildcard (*) that is a reserved charachter in URI's.
        // Replace with dummy localhost representation just for parsing purpose.
        if (endPoint.IndexOf("//*", StringComparison.Ordinal) != -1)
        {
            // FIXME: This is a workaround for the fact that Uri.Host is not set for wildcard host.
            throw new ArgumentException("Wildcard host is not supported for WebSocket endpoints");
        }
        else
        {
            uriToParse = endPoint;
        }

        string[] supportedSchemes = new string[] { "ws", "wss", "http", "https" };

        if (!string.IsNullOrEmpty(uriToParse) && Uri.TryCreate(uriToParse, UriKind.Absolute, out uri))
        {
            bool supported = false;
            foreach (string scheme in supportedSchemes)
            {
                if (string.Compare(uri.Scheme, scheme, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    supported = true;
                    break;
                }
            }
            if (!supported)
            {
                throw new ArgumentException(string.Format("Unsupported Uri schema, \"{0}\"", uri.Scheme));
            }
            return;
        }
        else
        {
            throw new ArgumentException(string.Format("Could not parse {0} into host, port", endPoint));
        }
    }


    // a coordination class that ensures we only start a single webserver even as the
    // diagnostic server protocol requires us to accept multiple connections.
    // while it is alive, each connection owns one reference to this singleton.
    // when the reference count goes from 0->1, we start the server.
    // when the references drops back to zero, we let the webserver stop.
    internal class Singleton
    {
        private volatile int _refCount;
        public bool ServerRunning { get; internal set; } = false;
        public bool ServerStopping { get; internal set; } = false;
        public WebSocketServer.IWebSocketServer server { get; internal set; } = null;
        internal Singleton()
        {
            _refCount = 0;
        }

        public bool AddRef()
        {
            return Interlocked.Increment(ref _refCount) == 1;
        }

        public void DropRef()
        {
            if (_refCount == 0)
            {
                throw new InvalidOperationException("DropRef called more times than AddRef");
            }
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                StopServer().Wait();
            }
        }

        internal async Task StartServer(Uri endPoint, CancellationToken token)
        {
            if (ServerStopping)
            {
                return;
            }
            ServerRunning = true;
            WebSocketServer.IWebSocketServer newServer = WebSocketServer.WebSocketServerFactory.CreateWebSocketServer();
            server = newServer; ;
            await server.StartServer(endPoint, token);
            await Task.Delay(1000);
        }

        internal async Task StopServer(CancellationToken token = default)
        {
            ServerStopping = true;
            await server.StopServer(token);
            ServerRunning = false;
        }

        internal async Task<Stream> AcceptConnection(CancellationToken token)
        {
            return await server.AcceptConnection(token);
        }
    }
}
