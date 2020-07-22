// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class MetricSourceConfiguration : MonitoringSourceConfiguration
    {
        public MetricSourceConfiguration(int metricIntervalSeconds = 60)
        {
            MetricIntervalSeconds = metricIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        }

        private string MetricIntervalSeconds { get; }

        public override IList<EventPipeProvider> GetProviders()
        {
            var providers = new List<EventPipeProvider>()
            {
                // Runtime Metrics
                new EventPipeProvider(
                    SystemRuntimeEventSourceName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                            { "EventCounterIntervalSec", MetricIntervalSeconds }
                    }
                ),
                new EventPipeProvider(
                    MicrosoftAspNetCoreHostingEventSourceName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", MetricIntervalSeconds }
                    }
                ),
                new EventPipeProvider(
                    GrpcAspNetCoreServer,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", MetricIntervalSeconds }
                    }
                ),
                
                // Application Metrics
                //new EventPipeProvider(
                //    applicationName,
                //    EventLevel.Informational,
                //    (long)ClrTraceEventParser.Keywords.None,
                //    new Dictionary<string, string>() {
                //        { "EventCounterIntervalSec", MetricIntervalSeconds }
                //    }
                //),
            };

            return providers;
        }

        
    }
}
