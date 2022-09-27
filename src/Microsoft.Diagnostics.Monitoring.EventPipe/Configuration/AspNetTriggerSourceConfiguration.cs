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
    /// collecting just one causes unusual behavior in data collection. This value is identical to
    /// the DiagnosticFilterString in HttpRequestSourceConfiguration.
    /// </summary>
    /// <remarks>
    /// IMPORTANT! We rely on these transformations to make sure we can access relevant data
    /// by index. The order must match the data extracted in the triggers.
    /// </remarks>
    private const string DiagnosticFilterString =
            "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
                "Request.Scheme" +
                ";Request.Host" +
                ";Request.PathBase" +
                ";Request.QueryString" +
                ";Request.Path" +
                ";Request.Method" +
                ";ActivityStartTime=*Activity.StartTimeUtc.Ticks" +
                ";ActivityParentId=*Activity.ParentId" +
                ";ActivityId=*Activity.Id" +
                ";ActivitySpanId=*Activity.SpanId" +
                ";ActivityTraceId=*Activity.TraceId" +
                ";ActivityParentSpanId=*Activity.ParentSpanId" +
                ";ActivityIdFormat=*Activity.IdFormat" +
            "\r\n" +
            "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop@Activity1Stop:-" +
                "Response.StatusCode" +
                ";ActivityDuration=*Activity.Duration.Ticks" +
                ";ActivityId=*Activity.Id" +
            "\r\n" +
            "HttpHandlerDiagnosticListener/System.Net.Http.HttpRequestOut@Event:-" +
            "\r\n" +
            "HttpHandlerDiagnosticListener/System.Net.Http.HttpRequestOut.Start@Activity2Start:-" +
                "Request.RequestUri" +
                ";Request.Method" +
                ";Request.RequestUri.Host" +
                ";Request.RequestUri.Port" +
                ";ActivityStartTime=*Activity.StartTimeUtc.Ticks" +
                ";ActivityId=*Activity.Id" +
                ";ActivitySpanId=*Activity.SpanId" +
                ";ActivityTraceId=*Activity.TraceId" +
                ";ActivityParentSpanId=*Activity.ParentSpanId" +
                ";ActivityIdFormat=*Activity.IdFormat" +
                ";ActivityId=*Activity.Id" +
                "\r\n" +
            "HttpHandlerDiagnosticListener/System.Net.Http.HttpRequestOut.Stop@Activity2Stop:-" +
                ";ActivityDuration=*Activity.Duration.Ticks" +
                ";ActivityId=*Activity.Id" +
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
                        arguments: new Dictionary<string,string>
                        {
                            { "FilterAndPayloadSpecs", DiagnosticFilterString }
                        })
                };
            }
        }
    }
}
