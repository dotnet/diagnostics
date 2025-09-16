// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static partial class TraceEventExtensions
    {
        public static void GetLogMessageJsonEventData(
            this TraceEvent traceEvent,
            out LogMessageJsonEventData eventData)
        {
            Debug.Assert(traceEvent.EventName == "MessageJson");

            LogLevel logLevel = (LogLevel)traceEvent.PayloadValue(0);
            // int factoryId = (int)traceEvent.PayloadValue(1);
            string categoryName = (string)traceEvent.PayloadValue(2);
            int eventId = (int)traceEvent.PayloadValue(3);
            string eventName = (string)traceEvent.PayloadValue(4);
            string exceptionJson = (string)traceEvent.PayloadValue(5);
            string argsJson = (string)traceEvent.PayloadValue(6);
            string formattedMessage = (string)traceEvent.PayloadValue(7);

            // NOTE: The Microsoft-Extensions-Logging EventSource is created with
            // EventSourceSettings.EtwSelfDescribingEventFormat (TraceLogging). In TraceLogging,
            // the ETW event header Version is always 0 even if the [Event] attribute declares
            // Version=2. The three correlation fields (ActivityTraceId, ActivitySpanId,
            // ActivityTraceFlags) were added in .NET 9, and with TraceLogging their presence
            // is discoverable only via payload schema (field names), not header Version.
            // Therefore we detect by payload name instead of checking traceEvent.Version.
            string? activityTraceId = (string)traceEvent.PayloadByName("ActivityTraceId");
            string? activitySpanId = (string)traceEvent.PayloadByName("ActivitySpanId");
            string? activityTraceFlags = (string)traceEvent.PayloadByName("ActivityTraceFlags");

            eventData = new(
                traceEvent.TimeStamp,
                logLevel,
                categoryName,
                eventId,
                eventName,
                exceptionJson,
                argsJson,
                formattedMessage,
                activityTraceId,
                activitySpanId,
                activityTraceFlags);
        }
    }
}
