// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// Periodically gets metrics from the app, and persists these to a metrics store.
    /// </summary>
    internal sealed class MetricsService : BackgroundService
    {
        private EventCounterPipeline _counterPipeline;
        private readonly IDiagnosticServices _services;
        private readonly MetricsStoreService _store;
        private readonly MetricsOptions _options;

        public MetricsService(IDiagnosticServices services,
            IOptions<MetricsOptions> metricsOptions,
            MetricsStoreService metricsStore)
        {
            _store = metricsStore;
            _services = services;
            _options = metricsOptions.Value;
        }
        
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run( async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        //TODO In multi-process scenarios, how do we decide which process to choose?
                        //One possibility is to enable metrics after a request to begin polling for metrics
                        IProcessInfo pi = await _services.GetProcessAsync(filter: null, stoppingToken);

                        var client = new DiagnosticsClient(pi.EndpointInfo.Endpoint);

                        _counterPipeline = new EventCounterPipeline(client, new EventPipeCounterPipelineSettings
                        {
                            CounterGroups = Array.Empty<EventPipeCounterGroup>(),
                            Duration = Timeout.InfiniteTimeSpan,
                            RefreshInterval = TimeSpan.FromSeconds(_options.UpdateIntervalSeconds)
                        }, metricsLogger: new[] { new MetricsLogger(_store.MetricsStore) });

                        await _counterPipeline.RunAsync(stoppingToken);
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        //Most likely we failed to resolve the pid. Attempt to do this again.
                        if (_counterPipeline != null)
                        {
                            await _counterPipeline.DisposeAsync();
                        }
                        await Task.Delay(5000);
                    }
                }
            }, stoppingToken);
        }

        public override async void Dispose()
        {
            base.Dispose();
            if (_counterPipeline != null)
            {
                await _counterPipeline.DisposeAsync();
            }
        }
    }
}
