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

        /// <summary>
        /// Filter string for trigger data. Note that even though some triggers use start OR stop,
        /// collecting just one causes unusual behavior in data collection.
        /// </summary>
        /// <remarks>
        /// IMPORTANT! We rely on these transformations to make sure we can access relevant data
        /// by index. The order must match the data extracted in the triggers.
        /// </remarks>
        private const string DiagnosticFilterString =
                "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
                    "ActivityId=*Activity.Id" +
                    ";Request.Path" +
                    ";ActivityStartTime=*Activity.StartTimeUtc.Ticks" +
                    "\r\n" +
                "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop@Activity1Stop:-" +
                    "ActivityId=*Activity.Id" +
                    ";Request.Path" +
                    ";Response.StatusCode" +
                    ";ActivityDuration=*Activity.Duration.Ticks" +
                    "\r\n";

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
                            { "FilterAndPayloadSpecs", DiagnosticFilterString }
                        })
                };
            }
        }
    }
}
