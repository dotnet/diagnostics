// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CancellationToken = System.Threading.CancellationToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Diagnostics.NETCore.Client.WebSocketServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using System.Net.WebSockets;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;
using System.Linq;

namespace Microsoft.Diagnostics.WebSocketServer;


public class WebSocketServerImpl : IWebSocketServer
{
    private WebSocketServer _server = null;
    private readonly Queue<Conn> _acceptQueue = new Queue<Conn>();

    public WebSocketServerImpl() { }

    public async Task StartServer(Uri uri, CancellationToken cancellationToken)
    {
        WebSocketServer.Options options = new()
        {
            Scheme = uri.Scheme,
            Host = uri.Host,
            Port = uri.Port.ToString(),
            Path = uri.PathAndQuery,
        };
        _server = WebSocketServer.CreateWebServer(options, HandleWebSocket);

        await _server.StartWebServer(cancellationToken);
    }


    public async Task StopServer(CancellationToken cancellationToken)
    {
        await _server.StopWebServer(cancellationToken);
        _server = null;
    }

    public async Task HandleWebSocket(HttpContext context, WebSocket webSocket, CancellationToken cancellationToken)
    {
        Console.WriteLine("got a connection on the websocket");
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
        return _acceptQueue.Dequeue(cancellationToken);
    }

    public async Task<Stream> AcceptConnection(CancellationToken cancellationToken)
    {
        Conn conn = await GetOrRequestConnection(cancellationToken);
        return conn.GetStream();
    }

    // single-element queue
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

public interface IWebSocketConnectionHandler
{
    Task Handle(HttpContext context, WebSocket webSocket, CancellationToken cancellationToken);
}

public class WebSocketServer
{
    public record Options
    {
        public string Scheme { get; set; } = "http";
        public string Host { get; set; } = default;
        public string Path { get; set; } = default!;
        public string Port { get; set; } = default;


        public void Assign(Options other)
        {
            Scheme = other.Scheme;
            Host = other.Host;
            Port = other.Port;
            Path = other.Path;
        }
    }

    private readonly IHost _host;
    private WebSocketServer(IHost host)
    {
        _host = host;
    }

    private static string[] MakeUrls(string scheme, string host, string port) => new string[] { $"{scheme}://{host}:{port}" };
    public static WebSocketServer CreateWebServer(Options options, Func<HttpContext, WebSocket, CancellationToken, Task> connectionHandler)
    {
        var builder = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                /* FIXME: delegate to outer host's logging */
                logging.AddConsole().AddFilter(null, LogLevel.Debug);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.AddCors(o => o.AddPolicy("AnyCors", builder =>
                    {
                        builder.AllowAnyOrigin()
                            .AllowAnyMethod()
                            .AllowAnyHeader()
                            .WithExposedHeaders("*");
                    }));
                services.AddRouting();
                services.Configure<Options>(localOptions => localOptions.Assign(options));
            })
            .ConfigureWebHostDefaults(webHostBuilder =>
            {
                webHostBuilder.UseKestrel();
                webHostBuilder.Configure((/*context, */app) => ConfigureApplication(/*context,*/ app, connectionHandler));
                webHostBuilder.UseUrls(MakeUrls(options.Scheme, options.Host, options.Port));
            });

        var host = builder.Build();

        return new WebSocketServer(host);
    }

    private static void ConfigureApplication(/*WebHostBuilderContext context,*/ IApplicationBuilder app, Func<HttpContext, WebSocket, CancellationToken, Task> connectionHandler)
    {
        app.Use((context, next) =>
        {
            context.Response.Headers.Add("Cross-Origin-Embedder-Policy", "require-corp");
            context.Response.Headers.Add("Cross-Origin-Opener-Policy", "same-origin");
            return next();
        });

        app.UseCors("AnyCors");

        app.UseWebSockets();
        app.UseRouter(router =>
        {
            var options = router.ServiceProvider.GetRequiredService<IOptions<Options>>().Value;
            router.MapGet(options.Path, (context) => OnWebSocketGet(context, connectionHandler));
        });

    }

    public async Task StartWebServer(CancellationToken ct = default)
    {
        await _host.StartAsync(ct);
        var logger = _host.Services.GetRequiredService<ILogger<WebSocketServer>>();
        var ipAddressSecure = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        .Where(a => a.StartsWith("http:"))
        .Select(a => new Uri(a))
        .Select(uri => $"{uri.Host}:{uri.Port}")
        .FirstOrDefault();

        logger.LogInformation("ip address is {IpAddressSecure}", ipAddressSecure);

    }

    public async Task StopWebServer(CancellationToken ct = default)
    {
        await _host.StopAsync(ct);
    }

    private static bool NeedsClose(WebSocketState state)
    {
        return state switch
        {
            WebSocketState.Open | WebSocketState.Connecting => true,
            WebSocketState.Closed | WebSocketState.CloseReceived | WebSocketState.CloseSent => false,
            WebSocketState.Aborted => false,
            _ => true
        };
    }

    private static async Task OnWebSocketGet(HttpContext context, Func<HttpContext, WebSocket, CancellationToken, Task> connectionHandler)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        if (connectionHandler != null)
            await connectionHandler(context, socket, context.RequestAborted);
        else
            await Task.Delay(250);
        if (NeedsClose(socket.State))
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }
}
