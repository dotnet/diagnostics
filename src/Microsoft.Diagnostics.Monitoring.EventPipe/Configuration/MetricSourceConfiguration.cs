// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    [Flags]
    public enum MetricType
    {
        EventCounter = 0x1,
        Meter = 0x2
    }
    
    public sealed class MetricEventPipeProvider
    {
        public string Provider { get; set; }

        public float IntervalSeconds { get; set; }

        public MetricType Type { get; set; } = MetricType.EventCounter | MetricType.Meter;
    }

    public sealed class MetricSourceConfiguration : MonitoringSourceConfiguration
    {
        private readonly IList<EventPipeProvider> _eventPipeProviders;
        public string SessionId { get; private set; }

        public static readonly string[] DefaultProviders = new[] { SystemRuntimeEventSourceName, MicrosoftAspNetCoreHostingEventSourceName, GrpcAspNetCoreServer };

        public MetricSourceConfiguration(float metricIntervalSeconds, IEnumerable<string> customProviderNames)
            : this(metricIntervalSeconds, customProviderNames?.Any() == true ? CreateProviders(metricIntervalSeconds, customProviderNames) :
            CreateProviders(metricIntervalSeconds, DefaultProviders))
        {
        }

        public MetricSourceConfiguration(float metricIntervalSeconds, IEnumerable<MetricEventPipeProvider> customProviderNames, int maxHistograms = 20, int maxTimeSeries = 1000) {
            if (customProviderNames == null) {
                throw new ArgumentNullException(nameof(customProviderNames));
            }

            RequestRundown = false;
            MetricIntervalSeconds = metricIntervalSeconds.ToString(CultureInfo.InvariantCulture);

            _eventPipeProviders = customProviderNames.Where(provider => provider.Type.HasFlag(MetricType.EventCounter))
                .Select((MetricEventPipeProvider provider) => new EventPipeProvider(provider.Provider,
                           EventLevel.Informational,
                           (long)ClrTraceEventParser.Keywords.None,
                           new Dictionary<string, string>()
                           {
                                { "EventCounterIntervalSec", provider.IntervalSeconds.ToString(CultureInfo.InvariantCulture)}
                           })).ToList();

            IEnumerable<MetricEventPipeProvider> meterProviders = customProviderNames.Where(provider => provider.Type.HasFlag(MetricType.Meter));

            if (meterProviders.Any())
            {
                const long TimeSeriesValues = 0x2;
                string metrics = string.Join(',', meterProviders.Select(p => p.Provider));

                SessionId = Guid.NewGuid().ToString();

                EventPipeProvider metricsEventSourceProvider =
                    new EventPipeProvider("System.Diagnostics.Metrics", EventLevel.Informational, TimeSeriesValues,
                        new Dictionary<string, string>()
                        {
                        { "SessionId", SessionId },
                        { "Metrics", metrics },
                        { "RefreshInterval", MetricIntervalSeconds.ToString() },
                        { "MaxTimeSeries", maxTimeSeries.ToString() },
                        { "MaxHistograms", maxHistograms.ToString() }
                        }
                    );

                _eventPipeProviders = _eventPipeProviders.Append(metricsEventSourceProvider).ToArray();
            }
        }

        private static IEnumerable<MetricEventPipeProvider> CreateProviders(float metricIntervalSeconds, IEnumerable<string> customProviderNames) =>
            customProviderNames.Select(provider => new MetricEventPipeProvider {
                Provider = provider,
                IntervalSeconds = metricIntervalSeconds,
                Type = MetricType.EventCounter
            });


        private string MetricIntervalSeconds { get; }

        public override IList<EventPipeProvider> GetProviders() => _eventPipeProviders;
    }
}
