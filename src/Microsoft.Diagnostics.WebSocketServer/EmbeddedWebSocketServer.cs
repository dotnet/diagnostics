// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace Microsoft.Diagnostics.WebSocketServer;

// This is a simple embedded web server that listens for connections and only accepts web
// socket connections on a given path and hands them off to a handler callback.
// The code here configures a new generic host (IHost) with a lifetime that is controlled by
// the user of this class.
internal sealed class EmbeddedWebSocketServer
{
    public sealed record Options
    {
        public string Scheme { get; set; } = "http";
        public string Host { get; set; }
        public string Path { get; set; } = default!;
        public string Port { get; set; }
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        public void Assign(Options other)
        {
            Scheme = other.Scheme;
            Host = other.Host;
            Port = other.Port;
            Path = other.Path;
        }
    }

    private readonly IHost _host;
    private EmbeddedWebSocketServer(IHost host)
    {
        _host = host;
    }

    private static string[] MakeUrls(string scheme, string host, string port) => new string[] { $"{scheme}://{host}:{port}" };
    public static EmbeddedWebSocketServer CreateWebServer(Options options, Func<HttpContext, WebSocket, CancellationToken, Task> connectionHandler)
    {
        var builder = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                /* FIXME: use a delegating provider that sends the output to the dotnet-dsrouter LoggerFactory */
                logging.AddConsole().AddFilter(null, options.LogLevel);
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
            })
            .UseConsoleLifetime(options =>
            {
                options.SuppressStatusMessages = true;
            });

        var host = builder.Build();

        return new EmbeddedWebSocketServer(host);
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
        await _host.StartAsync(ct).ConfigureAwait(false);
        var logger = _host.Services.GetRequiredService<ILogger<EmbeddedWebSocketServer>>();
        var ipAddressSecure = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        .Where(a => a.StartsWith("http:"))
        .Select(a => new Uri(a))
        .Select(uri => $"{uri.Host}:{uri.Port}")
        .FirstOrDefault();

        logger.LogInformation("ip address is {IpAddressSecure}", ipAddressSecure);
    }

    public async Task StopWebServer(CancellationToken ct = default)
    {
        await _host.StopAsync(ct).ConfigureAwait(false);
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
        var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        if (connectionHandler != null)
        {
            await connectionHandler(context, socket, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            await Task.Delay(250).ConfigureAwait(false);
        }

        if (NeedsClose(socket.State))
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
        }
    }
}
