// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    public class LoggingSourceConfiguration : MonitoringSourceConfiguration
    {
        private const string DiagnosticFilterString = "\"" +
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
                "\r\n" +

                "\"";

        public override IList<EventPipeProvider> GetProviders()
        {
            var providers = new List<EventPipeProvider>()
            {
                // Logging
                new EventPipeProvider(
                    MicrosoftExtensionsLoggingProviderName,
                    EventLevel.LogAlways,
                    (long)(LoggingEventSource.Keywords.JsonMessage | LoggingEventSource.Keywords.FormattedMessage)
                ),

                // Activity correlation
                //new EventPipeProvider(TplEventSource,
                //        keywords: 0x80,
                //        eventLevel: EventLevel.LogAlways),

                // Diagnostic source events
                new EventPipeProvider(DiagnosticSourceEventSource,
                        keywords: 0x1 | 0x2,
                        eventLevel: EventLevel.Verbose,
                        arguments: new Dictionary<string,string>
                        {
                            { "FilterAndPayloadSpecs", DiagnosticFilterString }
                        })
            };

            return providers;
        }

        private sealed class LoggingEventSource
        {
            /// <summary>
            /// This is public from an EventSource consumer point of view, but since these defintions
            /// are not needed outside this class
            /// </summary>
            public static class Keywords
            {
                /// <summary>
                /// Meta events are events about the LoggingEventSource itself (that is they did not come from ILogger
                /// </summary>
                public const EventKeywords Meta = (EventKeywords)1;
                /// <summary>
                /// Turns on the 'Message' event when ILogger.Log() is called.   It gives the information in a programmatic (not formatted) way
                /// </summary>
                public const EventKeywords Message = (EventKeywords)2;
                /// <summary>
                /// Turns on the 'FormatMessage' event when ILogger.Log() is called.  It gives the formatted string version of the information.
                /// </summary>
                public const EventKeywords FormattedMessage = (EventKeywords)4;
                /// <summary>
                /// Turns on the 'MessageJson' event when ILogger.Log() is called.   It gives  JSON representation of the Arguments.
                /// </summary>
                public const EventKeywords JsonMessage = (EventKeywords)8;
            }
        }
    }
}
