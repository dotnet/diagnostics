// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static partial class TraceEventExtensions
    {
        private static readonly UTF8Encoding s_Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        [ThreadStatic]
        private static LogObject[]? s_ScopeStorage;
        [ThreadStatic]
        private static byte[]? s_JsonStorage;
        [ThreadStatic]
        private static KeyValuePair<string, object?>[]? s_AttributeStorage;

        public static void GetLogRecordPayloadFromMessageJsonEvent(
            this TraceEvent traceEvent,
            EventLogsPipeline.LogScopeItem? scopesLeafNode,
            out LogRecordPayload payload)
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

            int scopeCount = 0;
            if (scopesLeafNode != null)
            {
                ref LogObject[]? scopes = ref s_ScopeStorage;
                scopes ??= new LogObject[16];

                do
                {
                    AddToArrayGrowingAsNeeded(ref scopes, scopesLeafNode.ScopedObject, ref scopeCount);

                    scopesLeafNode = scopesLeafNode.Parent;
                }
                while (scopesLeafNode != null);
            }

            int numberOfAttributes = ParseAttributesFromJson(argsJson, out string? messageTemplate);
            ParseLogRecordExceptionFromJson(exceptionJson, out LogRecordException exception);

            if (!string.IsNullOrEmpty(formattedMessage)
                && formattedMessage.Equals(messageTemplate))
            {
                messageTemplate = null;
            }

            payload.LogRecord = new LogRecord(
                traceEvent.TimeStamp,
                categoryName,
                logLevel,
                new EventId(eventId, eventName),
                in exception,
                formattedMessage,
                messageTemplate,
                activityTraceId == null ? default : ActivityTraceId.CreateFromString(activityTraceId),
                activitySpanId == null ? default : ActivitySpanId.CreateFromString(activitySpanId),
                activityTraceFlags == "1"
                    ? ActivityTraceFlags.Recorded
                    : ActivityTraceFlags.None);

            payload.Attributes = new(s_AttributeStorage, 0, numberOfAttributes);

            payload.Scopes = new(s_ScopeStorage, 0, scopeCount);
        }

        private static void AddToArrayGrowingAsNeeded<T>(ref T[] destination, T item, ref int index)
        {
            if (index >= destination.Length)
            {
                T[] newArray = new T[destination.Length * 2];
                Array.Copy(destination, newArray, destination.Length);
                destination = newArray;
            }

            destination[index++] = item;
        }

        private static int ParseAttributesFromJson(string argumentsJson, out string? messageTemplate)
        {
            messageTemplate = null;

            if (argumentsJson == "{}")
            {
                return 0;
            }

            ref KeyValuePair<string, object?>[]? attributes = ref s_AttributeStorage;
            attributes ??= new KeyValuePair<string, object?>[16];

            Memory<byte> jsonBytes = ParseJson(argumentsJson);

            Utf8JsonReader reader = new(jsonBytes.Span);

            int attributeCount = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (messageTemplate == null && reader.ValueTextEquals("{OriginalFormat}"))
                    {
                        if (!TryReadPropertyValue(ref reader, out messageTemplate))
                        {
                            break;
                        }
                    }
                    else
                    {
                        string key = reader.GetString()!;

                        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                        {
                            break;
                        }

                        string value = reader.GetString()!;

                        AddToArrayGrowingAsNeeded(ref attributes, new(key, value), ref attributeCount);
                    }
                }
            }

            return attributeCount;
        }

        private static void ParseLogRecordExceptionFromJson(string exceptionJson, out LogRecordException exception)
        {
            if (exceptionJson == "{}")
            {
                exception = default;
                return;
            }

            Memory<byte> jsonBytes = ParseJson(exceptionJson);

            Utf8JsonReader reader = new(jsonBytes.Span);

            string? exceptionType = null;
            string? message = null;
            string? stackTrace = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("TypeName"))
                    {
                        if (TryReadPropertyValue(ref reader, out exceptionType))
                        {
                            continue;
                        }
                    }
                    else if (reader.ValueTextEquals("Message"))
                    {
                        if (TryReadPropertyValue(ref reader, out message))
                        {
                            continue;
                        }
                    }
                    else if (reader.ValueTextEquals("VerboseMessage"))
                    {
                        if (TryReadPropertyValue(ref reader, out stackTrace))
                        {
                            continue;
                        }
                    }
                }

                break;
            }

            exception = new(exceptionType, message, stackTrace);
        }

        private static bool TryReadPropertyValue(ref Utf8JsonReader reader, [NotNullWhen(true)] out string? propertyValue)
        {
            if (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                propertyValue = reader.GetString()!;
                return true;
            }

            propertyValue = null;
            return false;
        }

        private static Memory<byte> ParseJson(string json)
        {
            ref byte[]? utf8 = ref s_JsonStorage;
            utf8 ??= new byte[8192];

            while (true)
            {
                int actualBytes = s_Utf8Encoding.GetBytes(json, utf8);
                if (actualBytes < 0)
                {
                    utf8 = new byte[utf8.Length * 2];
                    continue;
                }

                return new(utf8, 0, actualBytes);
            }
        }
    }
}
