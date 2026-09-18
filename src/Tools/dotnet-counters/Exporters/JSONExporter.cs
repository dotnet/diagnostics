// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.Monitoring.EventPipe;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    internal class JSONExporter : ICounterRenderer
    {
        private readonly object _lock = new();
        private readonly string _output;
        private readonly string _processName;
        private StringBuilder builder;
        private readonly int flushLength = 10_000; // Arbitrary length to flush

        public JSONExporter(string output, string processName)
        {
            if (output.EndsWith(".json"))
            {
                _output = output;
            }
            else
            {
                _output = output + ".json";
            }
            _processName = processName;
        }
        public void Initialize()
        {
            if (File.Exists(_output))
            {
                Console.WriteLine($"[Warning] {_output} already exists. This file will be overwritten.");
                File.Delete(_output);
            }

            lock (_lock)
            {
                builder = new StringBuilder();
                builder
                    .Append("{ \"TargetProcess\": \"").Append(_processName).Append("\", ")
                    .Append("\"StartTime\": \"").Append(DateTime.Now.ToString("O")).Append("\", ")
                    .Append("\"Events\": [");
            }
        }

        public void EventPipeSourceConnected()
        {
            Console.WriteLine("Starting a counter session. Press Q to quit.");
        }

        public void SetErrorText(string errorText)
        {
            Console.WriteLine(errorText);
        }

        public void ToggleStatus(bool paused)
        {
            // Do nothing
        }

        public void CounterPayloadReceived(CounterPayload payload, bool _)
        {
            lock (_lock)
            {
                if (builder.Length > flushLength)
                {
                    File.AppendAllText(_output, builder.ToString());
                    builder.Clear();
                }
                builder
                    .Append("{ \"timestamp\": \"").Append(DateTime.Now.ToString("O")).Append("\", ")
                    .Append(" \"provider\": \"").Append(JsonEscape(payload.CounterMetadata.ProviderName)).Append("\", ")
                    .Append(" \"name\": \"").Append(JsonEscape(payload.GetDisplay())).Append("\", ")
                    .Append(" \"tags\": \"").Append(JsonEscape(FormatTags(payload.ValueTags, payload.IsMeter))).Append("\", ")
                    .Append(" \"counterType\": \"").Append(JsonEscape(payload.CounterType.ToString())).Append("\", ")
                    .Append(" \"meterTags\": \"").Append(JsonEscape(FormatTags(payload.CounterMetadata.MeterTags, payload.IsMeter))).Append("\", ")
                    .Append(" \"instrumentTags\": \"").Append(JsonEscape(FormatTags(payload.CounterMetadata.InstrumentTags, payload.IsMeter))).Append("\", ")
                    .Append(" \"value\": ").Append(payload.Value.ToString(CultureInfo.InvariantCulture)).Append(" },");
            }
        }

        public void CounterStopped(CounterPayload payload) { }

        // Renders decoded tags as the flat "key=value,key=value" string this exporter emits. The values
        // are unescaped; JSON string quoting handles any ',' or '=' they contain.
        private static string FormatTags(string tags, bool isMeter)
        {
            if (!isMeter)
            {
                return tags;
            }

            StringBuilder sb = new();
            foreach (KeyValuePair<string, string> tag in CounterTagFormatter.Decode(tags))
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }

                sb.Append(tag.Key).Append('=').Append(tag.Value);
            }

            return sb.ToString();
        }

        public void Stop()
        {
            lock (_lock)
            {
                builder.Remove(builder.Length - 1, 1); // Remove the last comma to ensure valid JSON format.
                builder.Append("]}");
                // Append all the remaining text to the file.
                File.AppendAllText(_output, builder.ToString());
            }
            Console.WriteLine("File saved to " + _output);
        }

        // Escapes a string for embedding in a JSON string literal. The named short escapes are used for
        // the common control characters; every other character below U+0020 is emitted as \u00XX because
        // JSON (RFC 8259) forbids raw control characters in a string and a strict parser would otherwise
        // reject the document.
        private static string JsonEscape(string input)
        {
            if (input is null)
            {
                return string.Empty;
            }

            if (IndexOfEscapable(input) == -1)
            {
                // fast path
                return input;
            }

            // slow path
            // this could be written more efficiently but I expect it to be quite rare and not performance sensitive
            // so I didn't feel justified writing a complex routine or adding a few 100KB for a dependency on a
            // better performing JSON library
            StringBuilder sb = new(input.Length + 10);
            foreach (char c in input)
            {
                switch (c)
                {
                    case '\"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    default:
                        if (c < '\u0020')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        // Returns the index of the first character that must be escaped in a JSON string, or -1 if none.
        // '"' and '\' are structural; every character below U+0020 is a control character JSON forbids raw.
        private static int IndexOfEscapable(string input)
        {
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '"' || c == '\\' || c < '\u0020')
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
