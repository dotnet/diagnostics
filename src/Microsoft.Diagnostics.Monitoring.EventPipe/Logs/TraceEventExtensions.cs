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

            string? activityTraceId;
            string? activitySpanId;
            string? activityTraceFlags;
            if (traceEvent.Version >= 2)
            {
                // Note: Trace correlation fields added in .NET 9
                activityTraceId = (string)traceEvent.PayloadValue(8);
                activitySpanId = (string)traceEvent.PayloadValue(9);
                activityTraceFlags = (string)traceEvent.PayloadValue(10);
            }
            else
            {
                activityTraceId = null;
                activitySpanId = null;
                activityTraceFlags = null;
            }

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
