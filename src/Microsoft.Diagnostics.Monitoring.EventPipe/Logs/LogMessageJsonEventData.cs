// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal readonly struct LogMessageJsonEventData
    {
        public DateTime Timestamp { get; }
        public LogLevel LogLevel { get; }
        public string CategoryName { get; }
        public int EventId { get; }
        public string EventName { get; }
        public string ExceptionJson { get; }
        public string ArgumentsJson { get; }
        public string FormattedMessage { get; }
        public string? ActivityTraceId { get; }
        public string? ActivitySpanId { get; }
        public string? ActivityTraceFlags { get; }

        public LogMessageJsonEventData(
            DateTime timestamp,
            LogLevel logLevel,
            string categoryName,
            int eventId,
            string eventName,
            string exceptionJson,
            string argumentsJson,
            string formattedMessage,
            string? activityTraceId,
            string? activitySpanId,
            string? activityTraceFlags)
        {
            Timestamp = timestamp;
            LogLevel = logLevel;
            CategoryName = categoryName;
            EventId = eventId;
            EventName = eventName;
            ExceptionJson = exceptionJson;
            ArgumentsJson = argumentsJson;
            FormattedMessage = formattedMessage;
            ActivityTraceId = activityTraceId;
            ActivitySpanId = activitySpanId;
            ActivityTraceFlags = activityTraceFlags;
        }
    }
}
