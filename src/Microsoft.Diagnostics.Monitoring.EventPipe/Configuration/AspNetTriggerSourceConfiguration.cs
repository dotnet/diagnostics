// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class AspNetTriggerSourceConfiguration : MonitoringSourceConfiguration
    {
        // In order to handle hung requests, we also capture metrics on a regular interval.
        // This acts as a wake up timer, since we cannot rely on Activity1Stop.
        // Note this value is parameterizable due to limitations in event counters: only 1 interval duration is
        // respected. This gives the trigger infrastructure a way to use the same interval.
        private readonly float? _heartbeatIntervalSeconds;

        public AspNetTriggerSourceConfiguration(float? heartbeatIntervalSeconds = null)
        {
            RequestRundown = false;
            _heartbeatIntervalSeconds = heartbeatIntervalSeconds;
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            if (_heartbeatIntervalSeconds.HasValue)
            {
                return new AggregateSourceConfiguration(
                    new AspNetTriggerSourceConfiguration(heartbeatIntervalSeconds: null),
                    new MetricSourceConfiguration(_heartbeatIntervalSeconds.Value, new[] { MicrosoftAspNetCoreHostingEventSourceName })).GetProviders();

            }
            else
            {
                return new[]
                {
                    new EventPipeProvider(DiagnosticSourceEventSource,
                        keywords: DiagnosticSourceEventSourceEvents | DiagnosticSourceEventSourceMessages,
                        eventLevel: EventLevel.Verbose,
                        arguments: new Dictionary<string, string>
                        {
                            { "FilterAndPayloadSpecs", HttpRequestSourceConfiguration.DiagnosticFilterString }
                        })
                };
            }
        }
    }
}
