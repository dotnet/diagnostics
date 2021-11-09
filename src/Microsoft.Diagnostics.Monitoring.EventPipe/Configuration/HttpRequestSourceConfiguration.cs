// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class HttpRequestSourceConfiguration : MonitoringSourceConfiguration
    {
        public HttpRequestSourceConfiguration()
        {
            //CONSIDER removing rundown for this scenario.
            RequestRundown = true;
        }

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
            var providers = new List<EventPipeProvider>()
            {
                // Diagnostic source events
                new EventPipeProvider(DiagnosticSourceEventSource,
                        keywords: DiagnosticSourceEventSourceEvents | DiagnosticSourceEventSourceMessages,
                        eventLevel: EventLevel.Verbose,
                        arguments: new Dictionary<string,string>
                        {
                            { "FilterAndPayloadSpecs", DiagnosticFilterString }
                        })
            };

            return providers;
        }
    }
}
