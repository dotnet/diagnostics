// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal readonly struct LogRecord
    {
        public LogRecord(
            DateTime timestamp,
            string categoryName,
            LogLevel logLevel,
            EventId eventId,
            in LogRecordException exception,
            string? formattedMessage,
            string? messageTemplate,
            ActivityTraceId traceId,
            ActivitySpanId spanId,
            ActivityTraceFlags traceFlags)
        {
            if (string.IsNullOrEmpty(categoryName))
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            Timestamp = timestamp;
            CategoryName = categoryName;
            LogLevel = logLevel;
            EventId = eventId;
            Exception = exception;
            FormattedMessage = formattedMessage;
            MessageTemplate = messageTemplate;
            TraceId = traceId;
            SpanId = spanId;
            TraceFlags = traceFlags;
        }

        public readonly DateTime Timestamp;

        public readonly string CategoryName;

        public readonly LogLevel LogLevel;

        public readonly EventId EventId;

        public readonly LogRecordException Exception;

        public readonly string? FormattedMessage;

        public readonly string? MessageTemplate;

        public readonly ActivityTraceId TraceId;

        public readonly ActivitySpanId SpanId;

        public readonly ActivityTraceFlags TraceFlags;
    }
}
