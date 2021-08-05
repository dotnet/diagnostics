﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    class JSONExporter : ICounterRenderer
    {
        private object _lock = new object();
        private string _output;
        private string _processName;
        private StringBuilder builder;
        private int flushLength = 10_000; // Arbitrary length to flush

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
                    .Append(" \"provider\": \"").Append(JsonEscape(payload.ProviderName)).Append("\", ")
                    .Append(" \"name\": \"").Append(JsonEscape(payload.DisplayName)).Append("\", ")
                    .Append(" \"tags\": \"").Append(JsonEscape(payload.Tags)).Append("\", ")
                    .Append(" \"counterType\": \"").Append(JsonEscape(payload.CounterType)).Append("\", ")
                    .Append(" \"value\": ").Append(payload.Value.ToString(CultureInfo.InvariantCulture)).Append(" },");
            }
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

        static readonly char[] s_escapeChars = new char[] { '"', '\n', '\r', '\t', '\\', '\b', '\f' };

        private string JsonEscape(string input)
        {
            int offset = input.IndexOfAny(s_escapeChars);
            if(offset == -1)
            {
                // fast path
                return input;
            }

            // slow path
            // this could be written more efficiently but I expect it to be quite rare and not performance sensitive
            // so I didn't feel justified writing a complex routine or adding a few 100KB for a dependency on a
            // better performing JSON library
            StringBuilder sb = new StringBuilder(input.Length + 10);
            foreach(char c in input)
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
