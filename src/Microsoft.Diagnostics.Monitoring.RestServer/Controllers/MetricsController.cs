// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    [Route("")]
    [ApiController]
    public class MetricsController : ControllerBase
    {
        private readonly ILogger<MetricsController> _logger;
        private readonly MetricsStoreService _metricsStore;
        private readonly PrometheusConfiguration _prometheusConfiguration;

        public MetricsController(ILogger<MetricsController> logger,
            IServiceProvider serviceProvider,
            IOptions<PrometheusConfiguration> prometheusConfiguration)
        {
            _logger = logger;
            _metricsStore = serviceProvider.GetService<MetricsStoreService>();
            _prometheusConfiguration = prometheusConfiguration.Value;
        }

        [HttpGet("metrics")]
        public ActionResult Metrics()
        {
            return this.InvokeService(() =>
            {
                if (!_prometheusConfiguration.Enabled)
                {
                    throw new InvalidOperationException("Metrics was not enabled");
                }

                return new OutputStreamResult(async (outputStream, token) =>
                {
                    await _metricsStore.MetricsStore.SnapshotMetrics(outputStream, token);
                }, "text/plain; version=0.0.4");
            });
        }
    }
}
