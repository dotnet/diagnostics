// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class AspNetTriggerSourceConfiguration : MonitoringSourceConfiguration
    {
        // In order to handle hung requests, we also capture metrics on a regular interval.
        // This acts as a wake up timer, since we cannot rely on Activity1Stop.
        private readonly bool _supportHeartbeat;

        public const int DefaultHeartbeatInterval = 10;

        public AspNetTriggerSourceConfiguration(bool supportHeartbeat = false)
        {
            _supportHeartbeat = supportHeartbeat;
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
            if (_supportHeartbeat)
            {
                return new AggregateSourceConfiguration(
                    new AspNetTriggerSourceConfiguration(supportHeartbeat: false),
                    new MetricSourceConfiguration(DefaultHeartbeatInterval, new[] { MicrosoftAspNetCoreHostingEventSourceName })).GetProviders();

            }
            else
            {
                return new[]
                {
                    new EventPipeProvider(DiagnosticSourceEventSource,
                        keywords: 0x1 | 0x2,
                        eventLevel: EventLevel.Verbose,
                        arguments: new Dictionary<string,string>
                        {
                            { "FilterAndPayloadSpecs", DiagnosticFilterString }
                        })
                };
            }
        }
    }
}
