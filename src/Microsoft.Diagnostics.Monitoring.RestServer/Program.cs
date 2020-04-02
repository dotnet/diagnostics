using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    public class Program
    {
        public static IWebHostBuilder CreateWebHostBuilder(IDiagnosticServices diagServices) =>
            WebHost.CreateDefaultBuilder()
                .UseUrls("http://localhost:52323")
                .ConfigureServices((services) =>
                {
                    services.AddSingleton(diagServices);
                })
                .UseStartup<Startup>();
    }
}
