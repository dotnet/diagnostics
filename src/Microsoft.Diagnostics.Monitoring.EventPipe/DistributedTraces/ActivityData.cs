// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal readonly struct ActivityData
    {
        public ActivityData(
            ActivitySourceData source,
            string operationName,
            string? displayName,
            ActivityKind kind,
            ActivityTraceId traceId,
            ActivitySpanId spanId,
            ActivitySpanId parentSpanId,
            ActivityTraceFlags traceFlags,
            string? traceState,
            DateTime startTimeUtc,
            DateTime endTimeUtc,
            ActivityStatusCode status,
            string? statusDescription)
        {
            if (string.IsNullOrEmpty(operationName))
            {
                throw new ArgumentNullException(nameof(operationName));
            }

            Source = source;
            OperationName = operationName;
            DisplayName = displayName;
            Kind = kind;
            TraceId = traceId;
            SpanId = spanId;
            ParentSpanId = parentSpanId;
            TraceFlags = traceFlags;
            TraceState = traceState;
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            Status = status;
            StatusDescription = statusDescription;
        }

        public readonly ActivitySourceData Source;

        public readonly string OperationName;

        public readonly string? DisplayName;

        public readonly ActivityKind Kind;

        public readonly ActivityTraceId TraceId;

        public readonly ActivitySpanId SpanId;

        public readonly ActivitySpanId ParentSpanId;

        public readonly ActivityTraceFlags TraceFlags;

        public readonly string? TraceState;

        public readonly DateTime StartTimeUtc;

        public readonly DateTime EndTimeUtc;

        public readonly ActivityStatusCode Status;

        public readonly string? StatusDescription;
    }
}
