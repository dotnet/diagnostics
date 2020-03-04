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
    public sealed class MetricsLogger : IMetricsLogger, IAsyncDisposable
    {
        private readonly ILogger<DiagnosticsMonitor> _logger;
        private readonly MetricsConfiguration _metricConfig;
        private readonly ResourceConfiguration _resourceConfig;

        private readonly Channel<Metric> _metricChannel;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly MetricsRestClient _metricsRestClient;
        private readonly Task _processingTask;

        private int _disposed = 0;

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

            //TODO Limit this
            _metricChannel = Channel.CreateUnbounded<Metric>();
            _cancellationTokenSource = new CancellationTokenSource();
            _metricsRestClient = new MetricsRestClient(_metricConfig, _resourceConfig);

            _processingTask = Task.Run(() => ProcessAllData(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        public void LogMetrics(Metric metric)
        {
            //Sink was not configured properly, we do not log any data.
            if (_processingTask == null)
            {
                return;
            }

            //We're not locking here so it's possible we won't throw even if the object has begun disposal.
            //We handle this gracefully.
            ThrowIfDisposed();

            //If the channel is complete, we will not be able to write to it.
            _metricChannel.Writer.TryWrite(metric);
        }

        private async Task ProcessAllData(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Metric metric = null;
                try
                {
                    metric = await _metricChannel.Reader.ReadAsync(token);
                }
                catch (ChannelClosedException)
                {
                    return;
                }

                try
                {
                    await _metricsRestClient.SendMetric(metric, token);
                }
                catch (Exception e) when ((!(e is OperationCanceledException)) && (!(e is ObjectDisposedException)))
                {
                    _logger.LogError(e, e.Message);
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private void ThrowIfDisposed()
        {
            if (Interlocked.CompareExchange(ref _disposed, value: 1, comparand: 1) == 1)
            {
                throw new ObjectDisposedException(nameof(MetricsLogger));
            }
        }

        public void Dispose()
        {
            _ = DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, value: 1, comparand: 0) == 1)
            {
                return;
            }

            //Do not allow any more entries. This should force ReadAsync to throw.
            _metricChannel?.Writer.TryComplete();

            //Finish processing
            //TODO Consider limiting this to a certain amount of time.
            if (_processingTask != null)
            {
                await _processingTask;
            }
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _metricsRestClient?.Dispose();
        }
    }
}
