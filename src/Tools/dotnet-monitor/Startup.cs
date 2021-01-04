// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Diagnostics.Monitoring.RestServer.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO.Compression;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(options =>
                {
                    options.Filters.Add(new ProducesAttribute("application/json"));

                    options.EnableEndpointRouting = false;
                })
                .SetCompatibilityVersion(CompatibilityVersion.Latest)
                .AddApplicationPart(typeof(DiagController).Assembly);

            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var details = new ValidationProblemDetails(context.ModelState);
                    var result = new BadRequestObjectResult(details);
                    result.ContentTypes.Add("application/problem+json");
                    return result;
                };
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Optimal;
            });

            services.AddResponseCompression(configureOptions =>
            {
                configureOptions.Providers.Add<BrotliCompressionProvider>();
                configureOptions.MimeTypes = new List<string> { "application/octet-stream" };
            });

            // This is needed to allow the StreamingLogger to synchronously write to the output stream.
            // Eventually should switch StreamingLoggger to something that allows for async operations.
            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            var metricsOptions = new MetricsOptions();
            Configuration.Bind(MetricsOptions.ConfigurationKey, metricsOptions);
            if (metricsOptions.Enabled)
            {
                services.AddSingleton<MetricsStoreService>();
                services.AddHostedService<MetricsService>();
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            ExperimentalToolLogger logger)
        {
            logger.LogExperimentMessage();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            CorsConfiguration corsConfiguration = new CorsConfiguration();
            Configuration.Bind(nameof(CorsConfiguration), corsConfiguration);
            if (!string.IsNullOrEmpty(corsConfiguration.AllowedOrigins))
            {
                app.UseCors(builder => builder.WithOrigins(corsConfiguration.GetOrigins()).AllowAnyHeader().AllowAnyMethod());
            }

            app.UseResponseCompression();
            app.UseMvc();
        }
    }
}
