using Microsoft.Diagnostics.Tracing.Extensions;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsTCPIP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class MetricsLogger : IMetricsLogger
    {
        private readonly ILogger<MetricsLogger> _logger;
        private Channel<Metric> _metricChannel;
        private CancellationTokenSource _cancellationTokenSource;
        private MetricsRestClient _metricsRestClient;

        public MetricsLogger(ILogger<MetricsLogger> logger, IConfiguration config)
        {
            _metricChannel = Channel.CreateUnbounded<Metric>();
            _cancellationTokenSource = new CancellationTokenSource();
            _logger = logger;
            Task.Run(() => ProcessAllData(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        public void LogMetrics(Metric metric)
        {
            if (!_metricChannel.Writer.TryWrite(metric))
            {
                _logger.LogInformation("Failed to post metric {0}", metric.Name);
            }
        }

        private async Task ProcessAllData(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                Metric metric = await _metricChannel.Reader.ReadAsync(token);
                await _metricsRestClient.SendMetric(metric, token);
            }
        }

        public void Dispose()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            if (_metricsRestClient != null)
            {
                _metricsRestClient.Dispose();
                _metricsRestClient = null;
            }
            if (_metricChannel != null)
            {
                _metricChannel.Writer.TryComplete(null);
                _metricChannel = null;
            }
        }
    }
}
