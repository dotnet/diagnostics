// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    [Flags]
    public enum MetricType
    {
        EventCounter = 0x1,
        Meter = 0x2,
        All = 0xFF
    }

    public sealed class MetricEventPipeProvider
    {
        public string Provider { get; set; }

        public float? IntervalSeconds { get; set; }

        public MetricType Type { get; set; } = MetricType.All;
    }

    public sealed class MetricSourceConfiguration : MonitoringSourceConfiguration
    {
        private const string SharedSessionId = "SHARED";

        private readonly IList<EventPipeProvider> _eventPipeProviders;
        public string ClientId { get; private set; }
        public string SessionId { get; private set; }

        public MetricSourceConfiguration(float metricIntervalSeconds, IEnumerable<string> eventCounterProviderNames)
            : this(metricIntervalSeconds, CreateProviders(eventCounterProviderNames?.Any() == true ? eventCounterProviderNames : DefaultMetricProviders))
        {
        }

        public MetricSourceConfiguration(float metricIntervalSeconds, IEnumerable<MetricEventPipeProvider> providers, int maxHistograms = 20, int maxTimeSeries = 1000, bool useSharedSession = false)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            RequestRundown = false;

            _eventPipeProviders = providers.Where(provider => provider.Type.HasFlag(MetricType.EventCounter))
                .Select((MetricEventPipeProvider provider) => new EventPipeProvider(provider.Provider,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>()
                    {
                        {
                            "EventCounterIntervalSec", (provider.IntervalSeconds ?? metricIntervalSeconds).ToString(CultureInfo.InvariantCulture)
                        }
                    })).ToList();

            IEnumerable<MetricEventPipeProvider> meterProviders = providers.Where(provider => provider.Type.HasFlag(MetricType.Meter));

            if (meterProviders.Any())
            {
                const long TimeSeriesValuesEventKeyword = 0x2;
                string metrics = string.Join(',', meterProviders.Select(p => p.Provider));

                ClientId = Guid.NewGuid().ToString();

                // Shared Session Id was added in 8.0 - older runtimes will not properly support it.
                SessionId = useSharedSession ? SharedSessionId : Guid.NewGuid().ToString();

                EventPipeProvider metricsEventSourceProvider =
                    new(MonitoringSourceConfiguration.SystemDiagnosticsMetricsProviderName, EventLevel.Informational, TimeSeriesValuesEventKeyword,
                        new Dictionary<string, string>()
                        {
                            { "SessionId", SessionId },
                            { "Metrics", metrics },
                            { "RefreshInterval", metricIntervalSeconds.ToString(CultureInfo.InvariantCulture) },
                            { "MaxTimeSeries", maxTimeSeries.ToString(CultureInfo.InvariantCulture) },
                            { "MaxHistograms", maxHistograms.ToString(CultureInfo.InvariantCulture) },
                            { "ClientId", ClientId  }
                        }
                    );

                _eventPipeProviders = _eventPipeProviders.Append(metricsEventSourceProvider).ToArray();
            }
        }

        internal static IEnumerable<MetricEventPipeProvider> CreateProviders(IEnumerable<string> providers, MetricType metricType = MetricType.EventCounter) =>
            providers.Select(provider => new MetricEventPipeProvider {
                Provider = provider,
                Type = metricType
            });

        public override IList<EventPipeProvider> GetProviders() => _eventPipeProviders;
    }
}
