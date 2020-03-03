using Microsoft.Diagnostics.Tracing.Extensions;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsTCPIP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    public sealed class MetricsLogger : IMetricsLogger
    {
        private readonly ILogger<DiagnosticsMonitor> _logger;
        private MetricsConfiguration _metricConfig;
        private ResourceConfiguration _resourceConfig;

        private Channel<Metric> _metricChannel;
        private CancellationTokenSource _cancellationTokenSource;
        private MetricsRestClient _metricsRestClient;

        public MetricsLogger(ILogger<DiagnosticsMonitor> logger,
            IOptions<MetricsConfiguration> metricsConfig,
            IOptions<ResourceConfiguration> resourceConfig)
        {
            _logger = logger;
            
            _metricConfig = metricsConfig.Value;
            if (string.IsNullOrEmpty(_metricConfig.AadClientId) ||
                string.IsNullOrEmpty(_metricConfig.AadClientSecret) ||
                string.IsNullOrEmpty(_metricConfig.TenantId))
            {
                _logger.LogError("Failed to bind metrics configuration. Metrics will not be collected.");
                return;
            }
            _resourceConfig = resourceConfig.Value;

            if (string.IsNullOrEmpty(_resourceConfig.AzureRegion) ||
                string.IsNullOrEmpty(_resourceConfig.AzureResourceId) ||
                string.IsNullOrEmpty(_metricConfig.TenantId))
            {
                _logger.LogError("Failed to bind azure resource configuration. Metrics will not be collected.");
                return;
            }

            _metricChannel = Channel.CreateUnbounded<Metric>();
            _cancellationTokenSource = new CancellationTokenSource();
            _metricsRestClient = new MetricsRestClient(_metricConfig, _resourceConfig);

            Task.Run(() => ProcessAllData(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        public void LogMetrics(Metric metric)
        {
            if (_metricsRestClient == null)
            {
                return;
            }

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

                try
                {
                    await _metricsRestClient.SendMetric(metric, token);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, e.Message);
                }
                
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
