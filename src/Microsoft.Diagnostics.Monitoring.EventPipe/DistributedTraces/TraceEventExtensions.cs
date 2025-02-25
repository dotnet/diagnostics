// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static partial class TraceEventExtensions
    {
        private const string DefaultTraceId = "00000000000000000000000000000000";
        private const string DefaultSpanId = "0000000000000000";

        [ThreadStatic]
        private static KeyValuePair<string, object?>[]? s_TagStorage;
        private static readonly Dictionary<string, ActivitySourceData> s_Sources = new(StringComparer.OrdinalIgnoreCase);

        public static bool TryGetActivityPayload(this TraceEvent traceEvent, out ActivityPayload payload)
        {
            if ("Activity/Stop".Equals(traceEvent.EventName))
            {
                string sourceName = (traceEvent.PayloadValue(0) as string) ?? string.Empty;
                string? activityName = traceEvent.PayloadValue(1) as string;
                Array? arguments = traceEvent.PayloadValue(2) as Array;

                if (string.IsNullOrEmpty(activityName)
                    || arguments == null)
                {
                    payload = default;
                    return false;
                }

                ActivitySourceData source;
                string? displayName = null;
                ActivityTraceId traceId = default;
                ActivitySpanId spanId = default;
                ActivitySpanId parentSpanId = default;
                ActivityTraceFlags traceFlags = default;
                string? traceState = null;
                ActivityKind kind = default;
                ActivityStatusCode status = ActivityStatusCode.Unset;
                string? statusDescription = null;
                string? sourceVersion = null;
                DateTime startTimeUtc = default;
                long durationTicks = 0;
                int tagCount = 0;

                foreach (IDictionary<string, object> arg in arguments)
                {
                    string? key = arg["Key"] as string;
                    object value = arg["Value"];

                    switch (key)
                    {
                        case "TraceId":
                            string? traceIdValue = value as string;
                            if (!string.IsNullOrEmpty(traceIdValue)
                                && traceIdValue != DefaultTraceId)
                            {
                                traceId = ActivityTraceId.CreateFromString(traceIdValue);
                            }
                            break;
                        case "SpanId":
                            string? spanIdValue = value as string;
                            if (!string.IsNullOrEmpty(spanIdValue)
                                && spanIdValue != DefaultSpanId)
                            {
                                spanId = ActivitySpanId.CreateFromString(spanIdValue);
                            }
                            break;
                        case "ParentSpanId":
                            string? parentSpanIdValue = value as string;
                            if (!string.IsNullOrEmpty(parentSpanIdValue)
                                && parentSpanIdValue != DefaultSpanId)
                            {
                                parentSpanId = ActivitySpanId.CreateFromString(parentSpanIdValue);
                            }
                            break;
                        case "ActivityTraceFlags":
                            if (value is string traceFlagsValue)
                            {
                                traceFlags = (ActivityTraceFlags)Enum.Parse(typeof(ActivityTraceFlags), traceFlagsValue);
                            }
                            break;
                        case "TraceStateString":
                            traceState = value as string;
                            break;
                        case "Kind":
                            if (value is string kindValue)
                            {
                                kind = (ActivityKind)Enum.Parse(typeof(ActivityKind), kindValue);
                            }
                            break;
                        case "DisplayName":
                            string? displayNameValue = value as string;
                            if (!string.IsNullOrEmpty(displayNameValue)
                                && displayNameValue != activityName)
                            {
                                displayName = displayNameValue;
                            }

                            break;
                        case "StartTimeTicks":
                            if (value is string startTimeUtcValue)
                            {
                                startTimeUtc = new DateTime(long.Parse(startTimeUtcValue), DateTimeKind.Utc);
                            }
                            break;
                        case "DurationTicks":
                            if (value is string durationTicksValue)
                            {
                                durationTicks = long.Parse(durationTicksValue);
                            }
                            break;
                        case "Status":
                            if (value is string statusValue)
                            {
                                status = (ActivityStatusCode)Enum.Parse(typeof(ActivityStatusCode), statusValue);
                            }
                            break;
                        case "StatusDescription":
                            statusDescription = value as string;
                            break;
                        case "Tags":
                            string? tagsValue = value as string;
                            if (!string.IsNullOrEmpty(tagsValue))
                            {
                                tagCount = ParseTags(tagsValue);
                            }
                            break;
                        case "ActivitySourceVersion":
                            sourceVersion = value as string;
                            break;
                    }
                }

                if (!s_Sources.TryGetValue(sourceName, out source))
                {
                    source = new(sourceName, sourceVersion);
                    s_Sources[sourceName] = source;
                }

                payload.ActivityData = new ActivityData(
                    source,
                    activityName,
                    displayName,
                    kind,
                    traceId,
                    spanId,
                    parentSpanId,
                    traceFlags,
                    traceState,
                    startTimeUtc,
                    startTimeUtc + TimeSpan.FromTicks(durationTicks),
                    status,
                    statusDescription);

                payload.Tags = new(s_TagStorage, 0, tagCount);

                return true;
            }

            payload = default;
            return false;
        }

        private static int ParseTags(string tagsValue)
        {
            ref KeyValuePair<string, object?>[]? tags = ref s_TagStorage;
            tags ??= new KeyValuePair<string, object?>[16];

            int tagCount = 0;

            for (int i = 0; i < tagsValue.Length; i++)
            {
                if (tagsValue[i++] != '[')
                {
                    break;
                }

                int commaPosition = tagsValue.IndexOf(',', i);
                if (commaPosition < 0)
                {
                    break;
                }

                string key = tagsValue.Substring(i, commaPosition - i);

                i = commaPosition + 2;

                int endPosition = tagsValue.IndexOf(']', i);
                if (endPosition < 0)
                {
                    break;
                }

                string value = tagsValue.Substring(i, endPosition - i);

                i = endPosition + 1;

                AddToArrayGrowingAsNeeded(ref tags, new(key, value), ref tagCount);
            }

            return tagCount;
        }

        internal static void AddToArrayGrowingAsNeeded<T>(ref T[] destination, T item, ref int index)
        {
            if (index >= destination.Length)
            {
                T[] newArray = new T[destination.Length * 2];
                Array.Copy(destination, newArray, destination.Length);
                destination = newArray;
            }

            destination[index++] = item;
        }
    }
}
