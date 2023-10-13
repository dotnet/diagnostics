// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
                    .Append("\"StartTime\": \"").Append(DateTime.Now.ToString()).Append("\", ")
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
                    .Append("{ \"timestamp\": \"").Append(DateTime.Now.ToString("u")).Append("\", ")
                    .Append(" \"provider\": \"").Append(JsonEscape(payload.Provider.ProviderName)).Append("\", ")
                    .Append(" \"name\": \"").Append(JsonEscape(payload.GetDisplay())).Append("\", ")
                    .Append(" \"tags\": \"").Append(JsonEscape(payload.Metadata)).Append("\", ")
                    .Append(" \"counterType\": \"").Append(JsonEscape(payload.CounterType.ToString())).Append("\", ")
                    .Append(" \"meterTags\": \"").Append(JsonEscape(payload.Provider.MeterTags)).Append("\", ")
                    .Append(" \"instrumentTags\": \"").Append(JsonEscape(payload.Provider.InstrumentTags)).Append("\", ")
                    .Append(" \"value\": ").Append(payload.Value.ToString(CultureInfo.InvariantCulture)).Append(" },");
            }
        }

        public void CounterStopped(CounterPayload payload) { }

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

        private static readonly char[] s_escapeChars = new char[] { '"', '\n', '\r', '\t', '\\', '\b', '\f' };

        private static string JsonEscape(string input)
        {
            if (input is null)
            {
                return string.Empty;
            }

            int offset = input.IndexOfAny(s_escapeChars);
            if (offset == -1)
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
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
