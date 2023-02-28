using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    /// <summary>
    /// CONSIDER We can't reuse StreamingLoggerProvider from using Microsoft.Diagnostics.Monitoring.WebApi.
    /// Adding a reference to that project causes assembly resolution issues.
    /// </summary>
    internal sealed class TestStreamingLoggerProvider : ILoggerProvider
    {
        private readonly Stream _outputStream;
        private readonly LogLevel _logLevel;

        public TestStreamingLoggerProvider(Stream outputStream, LogLevel logLevel = LogLevel.Debug)
        {
            _outputStream = outputStream;
            _logLevel = logLevel;
        }

        public ILogger CreateLogger(string categoryName) => new TestStreamingLogger(categoryName, _outputStream, _logLevel);

        public void Dispose() { }
    }

    internal sealed class ScopeState : IEnumerable<IReadOnlyList<KeyValuePair<string, object>>>
    {
        private readonly Stack<IReadOnlyList<KeyValuePair<string, object>>> _scopes = new Stack<IReadOnlyList<KeyValuePair<string, object>>>();

        private sealed class ScopeEntry : IDisposable
        {
            private readonly Stack<IReadOnlyList<KeyValuePair<string, object>>> _scopes;

            public ScopeEntry(Stack<IReadOnlyList<KeyValuePair<string, object>>> scopes, IReadOnlyList<KeyValuePair<string, object>> scope)
            {
                _scopes = scopes;
                _scopes.Push(scope);
            }

            public void Dispose() => _scopes.Pop();
        }

        public IDisposable Push(IReadOnlyList<KeyValuePair<string, object>> scope) => new ScopeEntry(_scopes, scope);

        public IEnumerator<IReadOnlyList<KeyValuePair<string, object>>> GetEnumerator() => _scopes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class TestStreamingLogger : ILogger
    {
        private readonly ScopeState _scopes = new ScopeState();
        private readonly Stream _outputStream;
        private readonly string _categoryName;
        private readonly LogLevel _logLevel;

        public TestStreamingLogger(string category, Stream outputStream, LogLevel logLevel)
        {
            _outputStream = outputStream;
            _categoryName = category;
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
            Stream outputStream = _outputStream;

            using (var jsonWriter = new Utf8JsonWriter(outputStream, new JsonWriterOptions { Indented = false }))
            {
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString("LogLevel", logLevel.ToString());
                jsonWriter.WriteNumber("EventId", eventId.Id);
                jsonWriter.WriteString("EventName", eventId.Name ?? string.Empty);
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
