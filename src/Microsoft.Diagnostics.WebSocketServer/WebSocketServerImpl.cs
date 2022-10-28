// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client.WebSocketServer;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

using System.Net.WebSockets;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace Microsoft.Diagnostics.WebSocketServer;

// This class implements the IWebSocketServer interface exposed by the Microsoft.Diagnostics.NETCore.Client library.
// It is responsible for coordinating between an underlying web server that creates web socket connections and the diagnostic server router that
// is used by dotnet-dsrouter to pass the diagnostic server connections to the diagnostic clients.
public class WebSocketServerImpl : IWebSocketServer
{
    private EmbeddedWebSocketServer _server = null;
    private volatile int _started = 0;

    // Used to coordinate between the webserver accepting incoming websocket connections and the diagnostic server waiting for a stream to be available.
    // This could be a deeper queue if we wanted to somehow allow multiple browser tabs to connect to the same dsrouter, but it's unclear what to do with them
    // since on the other end we have a single IpcStream with a single diagnostic client.
    private readonly Queue<Conn> _acceptQueue = new Queue<Conn>();
    private readonly LogLevel _logLevel;

    public WebSocketServerImpl(LogLevel logLevel)
    {
        _logLevel = logLevel;
    }

    public async Task StartServer(string endpoint, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("Server already started");
        }

        ParseWebSocketURL(endpoint, out Uri uri);

        EmbeddedWebSocketServer.Options options = new()
        {
            Scheme = uri.Scheme,
            Host = uri.Host,
            Port = uri.Port.ToString(),
            Path = uri.PathAndQuery,
            LogLevel = _logLevel,
        };
        _server = EmbeddedWebSocketServer.CreateWebServer(options, HandleWebSocket);

        await _server.StartWebServer(cancellationToken);
    }

    public async Task StopServer(CancellationToken cancellationToken)
    {
        if (_started == 0)
        {
            throw new InvalidOperationException("Server not started");
        }
        if (_server == null)
            return;
        await _server.StopWebServer(cancellationToken);
        _server = null;
    }

    public async Task HandleWebSocket(HttpContext context, WebSocket webSocket, CancellationToken cancellationToken)
    {
        // Called by the web server when a new websocket connection is established.  We put the connection into our queue of accepted connections
        // and wait until someone uses it and disposes of the connection.
        await QueueWebSocketUntilClose(context, webSocket, cancellationToken);
    }

    internal async Task QueueWebSocketUntilClose(HttpContext context, WebSocket webSocket, CancellationToken cancellationToken)
    {
        // we have to "keep the middleware alive" until we're done with the websocket.
        // make a TCS that will be signaled when the stream is disposed.
        var streamDisposedTCS = new TaskCompletionSource(cancellationToken);
        await _acceptQueue.Enqueue(new Conn(context, webSocket, streamDisposedTCS), cancellationToken);
        await streamDisposedTCS.Task;
    }

    internal Task<Conn> GetOrRequestConnection(CancellationToken cancellationToken)
    {
        // This is called from the diagnostic server when it is ready to start talking to a connection. We give them back a connection from
        // the ones the web server has accepted, or block until the web server queues a new one.
        return _acceptQueue.Dequeue(cancellationToken);
    }

    public async Task<Stream> AcceptConnection(CancellationToken cancellationToken)
    {
        Conn conn = await GetOrRequestConnection(cancellationToken);
        return conn.GetStream();
    }

    // Single-element queue where both queueing and dequeueing are async operations that wait until
    // the queue has capacity (or an item, respectively).
    internal class Queue<T>
    {
        private T _obj;
        private readonly SemaphoreSlim _empty;
        private readonly SemaphoreSlim _full;
        private readonly SemaphoreSlim _objLock;

        public Queue()
        {
            _obj = default;
            int capacity = 1;
            _empty = new SemaphoreSlim(capacity, capacity);
            _full = new SemaphoreSlim(0, capacity);
            _objLock = new SemaphoreSlim(1, 1);
        }

        public async Task Enqueue(T t, CancellationToken cancellationToken)
        {
            bool locked = false;
            try
            {
                await _empty.WaitAsync(cancellationToken);
                await _objLock.WaitAsync(cancellationToken);
                locked = true;
                _obj = t;
            }
            finally
            {
                if (locked)
                {
                    _objLock.Release();
                    _full.Release();
                }
            }
        }

        public async Task<T> Dequeue(CancellationToken cancellationToken)
        {
            bool locked = false;
            try
            {
                await _full.WaitAsync(cancellationToken);
                await _objLock.WaitAsync(cancellationToken);
                locked = true;
                T t = _obj;
                _obj = default;
                return t;
            }
            finally
            {
                if (locked)
                {
                    _objLock.Release();
                    _empty.Release();
                }
            }
        }
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



    // An abstraction encapsulating an open websocket connection.
    internal class Conn
    {
        private readonly WebSocket _webSocket;
        private readonly HttpContext _context;
        private readonly TaskCompletionSource _streamDisposed;

        public Conn(HttpContext context, WebSocket webSocket, TaskCompletionSource streamDisposed)
        {
            _context = context;
            _webSocket = webSocket;
            _streamDisposed = streamDisposed;
        }

        public Stream GetStream()
        {
            return new WebSocketStreamAdapter(_webSocket, OnStreamDispose);
        }

        private void OnStreamDispose()
        {
            _streamDisposed.SetResult();
        }
    }
}
