// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Microsoft.Diagnostics.Monitoring.Logging
{
    /// <summary>
    /// This class is used to write structured event data in json format to an output stream.
    /// </summary>
    public sealed class StreamingLoggerProvider : ILoggerProvider
    {
        private readonly Stream _outputStream;

        public StreamingLoggerProvider(Stream outputStream)
        {
            _outputStream = outputStream;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new StreamingLogger(categoryName, _outputStream);
        }

        public void Dispose()
        {
        }
    }

    public sealed class StreamingLogger : ILogger
    {
        private Stack<IReadOnlyList<KeyValuePair<string, object>>> _scopes = new Stack<IReadOnlyList<KeyValuePair<string, object>>>();
        private readonly Stream _outputStream;
        private readonly string _categoryName;

        public StreamingLogger(string category, Stream outputStream)
        {
            _outputStream = outputStream;
            _categoryName = category;
        }

        private sealed class ScopeState : IDisposable
        {
            private readonly Stack<IReadOnlyList<KeyValuePair<string, object>>> _scopes;

            public ScopeState(Stack<IReadOnlyList<KeyValuePair<string, object>>> scopes, IReadOnlyList<KeyValuePair<string, object>> scope)
            {
                _scopes = scopes;
                _scopes.Push(scope);
            }

            public void Dispose()
            {
                _scopes.Pop();
            }
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            if (state is LogObject logObject)
            {
                return new ScopeState(_scopes, logObject);
            }
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Stream outputStream = _outputStream;

            //CONSIDER Should we cache up the loggers and writers?
            using (var jsonWriter = new Utf8JsonWriter(outputStream, new JsonWriterOptions { Indented = false }))
            {
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString("LogLevel", logLevel.ToString());
                jsonWriter.WriteString("EventId", eventId.ToString());
                jsonWriter.WriteString("Category", _categoryName);
                if (exception != null)
                {
                    jsonWriter.WriteString("Exception", formatter(state, exception));
                }
                else
                {
                    jsonWriter.WriteString("Message", formatter(state, exception));
                }

                //Write out scope data
                jsonWriter.WriteStartObject("Scopes");
                foreach (IReadOnlyList<KeyValuePair<string, object>> scope in _scopes)
                {
                    foreach(KeyValuePair<string, object> scopeValue in scope)
                    {
                        WriteKeyValuePair(jsonWriter, scopeValue);
                    }
                }
                jsonWriter.WriteEndObject();

                //Write out structured data
                jsonWriter.WriteStartObject("Arguments");
                if (state is IEnumerable<KeyValuePair<string, object>> values)
                {
                    foreach (KeyValuePair<string, object> arg in values)
                    {
                        WriteKeyValuePair(jsonWriter, arg);
                    }
                }
                jsonWriter.WriteEndObject();

                jsonWriter.WriteEndObject();
                jsonWriter.Flush();
            }

            outputStream.WriteByte((byte)'\n');
            outputStream.Flush();
        }

        private static void WriteKeyValuePair(Utf8JsonWriter jsonWriter, KeyValuePair<string, object> kvp)
        {
            jsonWriter.WritePropertyName(kvp.Key);
            switch (kvp.Value)
            {
                case string s:
                    jsonWriter.WriteStringValue(s);
                    break;
                case int i:
                    jsonWriter.WriteNumberValue(i);
                    break;
                case bool b:
                    jsonWriter.WriteBooleanValue(b);
                    break;
                case null:
                    jsonWriter.WriteNullValue();
                    break;
                default:
                    jsonWriter.WriteStringValue(kvp.Value.ToString());
                    break;
            }
        }
    }
}
