using Microsoft.Diagnostics.Monitoring;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class ConsoleSinkConfiguration : MonitoringSinkConfiguration
    {
        public override void AddLogger(ILoggingBuilder builder)
        {
            builder.AddConsole();
        }

        public override void AddMetricsLogger(IList<IMetricsLogger> metrics)
        {
            metrics.Add(new MetricsLogger());
        }

        private sealed class MetricsLogger : IMetricsLogger
        {
            public void LogMetrics(Metric metric)
            {
                string json = JsonSerializer.Serialize(metric, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true});
                Console.WriteLine(json);
            }
            public void Dispose()
            {
            }
        }
    }
}
