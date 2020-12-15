// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// This class is used to write structured event data in json format to an output stream.
    /// </summary>
    public sealed class StreamingLoggerProvider : ILoggerProvider
    {
        private readonly Stream _outputStream;
        private readonly LogFormat _format;
        private readonly LogLevel _logLevel;

        public StreamingLoggerProvider(Stream outputStream, LogFormat logFormat, LogLevel logLevel = LogLevel.Debug)
        {
            _outputStream = outputStream;
            _format = logFormat;
            _logLevel = logLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new StreamingLogger(categoryName, _outputStream, _format, _logLevel);
        }

        public void Dispose()
        {
        }
    }

    public sealed class StreamingLogger : ILogger
    {
        private readonly ScopeState _scopes = new ScopeState();
        private readonly Stream _outputStream;
        private readonly string _categoryName;
        private readonly LogFormat _logFormat;
        private readonly LogLevel _logLevel;

        public StreamingLogger(string category, Stream outputStream, LogFormat format, LogLevel logLevel)
        {
            _outputStream = outputStream;
            _categoryName = category;
            _logFormat = format;
            _logLevel = logLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            if (state is LogObject logObject)
            {
                return _scopes.Push(logObject);
            }
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel <= _logLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (_logFormat == LogFormat.Json)
            {
                LogJson(logLevel, eventId, state, exception, formatter);
            }
            else
            {
                LogEventStream(logLevel, eventId, state, exception, formatter);
            }
        }

        private void LogJson<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
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
                    foreach (KeyValuePair<string, object> scopeValue in scope)
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

        private void LogEventStream<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Stream outputStream = _outputStream;

            using var writer = new StreamWriter(outputStream, Encoding.UTF8, 1024, leaveOpen: true) { NewLine = "\n" };

            //event: eventName (if exists)
            //data: level category[eventId]
            //data: message
            //data: => scope1, scope2 => scope3, scope4
            //\n
            
            if (!string.IsNullOrEmpty(eventId.Name))
            {
                writer.Write("event: ");
                writer.WriteLine(eventId.Name);
            }
            writer.Write("data: ");
            writer.Write(logLevel);
            writer.Write(" ");
            writer.Write(_categoryName);
            writer.Write('[');
            writer.Write(eventId.Id);
            writer.WriteLine(']');
            writer.Write("data: ");
            writer.WriteLine(formatter(state, exception));

            bool firstScope = true;
            foreach (IReadOnlyList<KeyValuePair<string, object>> scope in _scopes)
            {
                bool firstScopeEntry = true;
                foreach (KeyValuePair<string, object> scopeValue in scope)
                {
                    if (firstScope)
                    {
                        writer.Write("data:");
                        firstScope = false;
                    }

                    if (firstScopeEntry)
                    {
                        writer.Write(" => ");
                        firstScopeEntry = false;
                    }
                    else
                    {
                        writer.Write(", ");
                    }
                    writer.Write(scopeValue.Key);
                    writer.Write(':');
                    writer.Write(scopeValue.Value);
                }
            }
            if (!firstScope)
            {
                writer.WriteLine();
            }
            writer.WriteLine();
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
