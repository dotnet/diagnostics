// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal sealed class LogRecordFactory
    {
        private static readonly UTF8Encoding s_Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private LogObject[] _ScopeStorage = new LogObject[16];
        private byte[] _JsonStorage = new byte[8192];
        private KeyValuePair<string, object?>[] _AttributeStorage = new KeyValuePair<string, object?>[16];

        public void EmitLog(
            ILogRecordLogger logger,
            EventLogsPipeline.LogScopeItem? scopesLeafNode,
            in LogMessageJsonEventData eventData)
        {
            int scopeCount = 0;
            if (scopesLeafNode != null)
            {
                ref LogObject[] scopes = ref _ScopeStorage;

                do
                {
                    TraceEventExtensions.AddToArrayGrowingAsNeeded(ref scopes, scopesLeafNode.ScopedObject, ref scopeCount);

                    scopesLeafNode = scopesLeafNode.Parent;
                }
                while (scopesLeafNode != null);
            }

            int numberOfAttributes = ParseAttributesFromJson(eventData.ArgumentsJson, out string? messageTemplate);
            ParseLogRecordExceptionFromJson(eventData.ExceptionJson, out LogRecordException exception);

            if (!string.IsNullOrEmpty(eventData.FormattedMessage)
                && eventData.FormattedMessage.Equals(messageTemplate))
            {
                messageTemplate = null;
            }

            LogRecord LogRecord = new(
                eventData.Timestamp,
                eventData.CategoryName,
                eventData.LogLevel,
                new EventId(eventData.EventId, eventData.EventName),
                in exception,
                eventData.FormattedMessage,
                messageTemplate,
                string.IsNullOrEmpty(eventData.ActivityTraceId) ? default : ActivityTraceId.CreateFromString(eventData.ActivityTraceId),
                string.IsNullOrEmpty(eventData.ActivitySpanId)  ? default : ActivitySpanId.CreateFromString(eventData.ActivitySpanId),
                ParseActivityTraceFlags(eventData.ActivityTraceFlags));

            ReadOnlySpan<KeyValuePair<string, object?>> Attributes = new(_AttributeStorage, 0, numberOfAttributes);

            ReadOnlySpan<LogObject> Scopes = new(_ScopeStorage, 0, scopeCount);

            logger.Log(
                in LogRecord,
                Attributes,
                new(Scopes));
        }

        // Newer Microsoft.Extensions.Logging.EventSource versions (net 11+ via runtime#124851)
        // emit the ActivityTraceFlags field as the integer value (e.g. "0", "1", "2", "3")
        // instead of just "0" or "1". Older versions emitted only "0"/"1". The empty string
        // indicates no current Activity. We only consume the Recorded bit; ignore other bits
        // such as RandomTraceId so this consumer remains compatible across versions.
        private static ActivityTraceFlags ParseActivityTraceFlags(string? value)
        {
            if (!string.IsNullOrEmpty(value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags)
                && (flags & (int)ActivityTraceFlags.Recorded) != 0)
            {
                return ActivityTraceFlags.Recorded;
            }
            return ActivityTraceFlags.None;
        }

        private int ParseAttributesFromJson(string argumentsJson, out string? messageTemplate)
        {
            messageTemplate = null;

            if (argumentsJson == "{}")
            {
                return 0;
            }

            ref KeyValuePair<string, object?>[] attributes = ref _AttributeStorage;

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

                        TraceEventExtensions.AddToArrayGrowingAsNeeded(ref attributes, new(key, value), ref attributeCount);
                    }
                }
            }

            return attributeCount;
        }

        private void ParseLogRecordExceptionFromJson(string exceptionJson, out LogRecordException exception)
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

        private Memory<byte> ParseJson(string json)
        {
            ref byte[] utf8 = ref _JsonStorage;

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
