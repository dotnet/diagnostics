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
    private readonly IpcWebSocketEndPoint _endPoint;
    public IpcWebSocketServerTransport(string address, int maxAllowedConnections, IIpcServerTransportCallbackInternal transportCallback = null)
        : base(transportCallback)
    {
        _maxConnections = maxAllowedConnections;
        _endPoint = new IpcWebSocketEndPoint(address);
        _cancellation = new CancellationTokenSource();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Cancel();

            // _stream.Dispose();
            singleton.DropRef();

            _cancellation.Dispose();
        }
    }

    public override async Task<Stream> AcceptAsync(CancellationToken token)
    {
        if (singleton.AddRef())
        {
            await singleton.StartServer(_endPoint, token);
            Console.WriteLine("starting server AAA");
        }
        Console.WriteLine("AcceptAsync");
        Stream s = await singleton.AcceptConnection(token);
        return s;
    }

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
                Console.WriteLine("stopping server AAA");
                StopServer().Wait();
            }
        }

        internal async Task StartServer(IpcWebSocketEndPoint endPoint, CancellationToken token)
        {
            if (ServerStopping)
            {
                return;
            }
            ServerRunning = true;
            string typeName = Environment.GetEnvironmentVariable("DIAGNOSTICS_SERVER_WEBSOCKET_SERVER_TYPE");
            Console.WriteLine("typeName: {0}", typeName);
            Type t = Type.GetType(typeName);
            if (t == null)
            {
                Console.WriteLine("no type found {0}", typeName);
                throw new Exception("Unable to find type " + typeName);
            }
            server = (WebSocketServer.IWebSocketServer)Activator.CreateInstance(t);
            await server.StartServer(endPoint.EndPoint, token);
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
