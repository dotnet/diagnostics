using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Net.Http;

namespace webapp
{
    public class Program
    {
        public static string PipeServerName;

        public static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                PipeServerName = args[0];
                Console.WriteLine("Pipe server: {0}", PipeServerName);
            }

            using (IWebHost host = CreateWebHostBuilder(args).Build())
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

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();
    }
}
