using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;

namespace WebApp3
{
    public class Program
    {
        [DllImport("DesktopClrHost.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int InitializeDesktopClrHost(string assemblyPath, string className, string functionName, string argument);

        public static string PipeServerName;

        public static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                PipeServerName = args[0];
                Console.WriteLine("Pipe server: {0}", PipeServerName);

                if (args.Length > 3)
                {
                    var thread = new Thread(() =>
                    {
                        Console.WriteLine("Starting desktop CLR: '{0} {1} {2}'", args[1], args[2], args[3]);
                        int hr = InitializeDesktopClrHost(args[1], args[2], args[3], args.Length > 4 ? args[4] : null);
                        if (hr != 0)
                        {
                            Console.WriteLine("Desktop CLR initialization FAILED: {0:X8}", hr);
                        }
                    });
                    thread.Start();
                }
            }

            using (IHost host = CreateHostBuilder(args).Build())
            {
                host.Start();

                using (var client = new HttpClient())
                {
                    string url = $"http://localhost:5000";
                    Console.WriteLine($"Starting request to {url}");
                    try
                    {
                        HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }

                host.WaitForShutdown();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
                webBuilder.UseStartup<Startup>());
    }
}
