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
    public sealed class MetricSourceConfiguration : MonitoringSourceConfiguration
    {
        private readonly IList<EventPipeProvider> _eventPipeProviders;

        public MetricSourceConfiguration(float metricIntervalSeconds, IEnumerable<string> customProviderNames)
        {
            RequestRundown = false;
            if (customProviderNames == null)
            {
                throw new ArgumentNullException(nameof(customProviderNames));
            }
            MetricIntervalSeconds = metricIntervalSeconds.ToString(CultureInfo.InvariantCulture);

            IEnumerable<string> providers = null;
            if (customProviderNames.Any())
            {
                providers = customProviderNames;
            }
            else
            {
                providers = new[] { SystemRuntimeEventSourceName, MicrosoftAspNetCoreHostingEventSourceName, GrpcAspNetCoreServer };
            }

            _eventPipeProviders = providers.Select((string provider) => new EventPipeProvider(provider,
               EventLevel.Informational,
               (long)ClrTraceEventParser.Keywords.None,
               new Dictionary<string, string>()
               {
                    { "EventCounterIntervalSec", MetricIntervalSeconds }
               })).ToList();
        }

        private string MetricIntervalSeconds { get; }

        public override IList<EventPipeProvider> GetProviders() => _eventPipeProviders;
    }
}
