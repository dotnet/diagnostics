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
    internal class CSVExporter : ICounterRenderer
    {
        private readonly object _lock = new(); // protects the StringBuilder instance.
        private readonly string _output;
        private StringBuilder builder;
        private readonly int flushLength = 10_000; // Arbitrary length to flush

        public string Output { get; set; }

        public CSVExporter(string output)
        {
            if (output.EndsWith(".csv"))
            {
                _output = output;
            }
            else
            {
                _output = output + ".csv";
            }
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
                builder.AppendLine("Timestamp,Provider,Counter Name,Counter Type,Mean/Increment");
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

                string counterName = payload.GetDisplay();
                string tags = payload.CombineTags();
                if (!string.IsNullOrEmpty(tags))
                {
                    counterName += "[" + FormatTags(tags, payload.IsMeter) + "]";
                }

                AppendField(builder, payload.Timestamp.ToString());
                builder.Append(',');
                AppendField(builder, payload.CounterMetadata.ProviderName);
                builder.Append(',');
                AppendField(builder, counterName);
                builder.Append(',');
                AppendField(builder, payload.CounterType.ToString());
                builder.Append(',');
                AppendField(builder, payload.Value.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');
            }
        }

        public void CounterStopped(CounterPayload payload) { }

        // Renders decoded tags into the single "[key=value;key=value]" column this exporter writes.
        // Pairs are separated by ';'. A decoded key or value may still contain a real ',': it is kept
        // as-is here because AppendField quotes the whole field per RFC 4180, so the comma cannot spill
        // into the next column.
        private static string FormatTags(string tags, bool isMeter)
        {
            if (!isMeter)
            {
                return tags.Replace(',', ';');
            }

            StringBuilder sb = new();
            foreach (KeyValuePair<string, string> tag in CounterTagFormatter.Decode(tags))
            {
                if (sb.Length > 0)
                {
                    sb.Append(';');
                }

                sb.Append(tag.Key).Append('=').Append(tag.Value);
            }

            return sb.ToString();
        }

        // Appends one field using RFC 4180 quoting: a field containing ',', '"', CR or LF is wrapped in
        // double quotes with any embedded '"' doubled. Every field is written through this so a comma in
        // a tag value, provider name, or counter display name cannot corrupt the row.
        private static void AppendField(StringBuilder builder, string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return;
            }

            if (field.IndexOfAny(s_csvSpecialCharacters) < 0)
            {
                builder.Append(field);
                return;
            }

            builder.Append('"').Append(field.Replace("\"", "\"\"")).Append('"');
        }

        private static readonly char[] s_csvSpecialCharacters = [',', '"', '\r', '\n'];

        public void Stop()
        {
            string outputString;
            // Append all the remaining text to the file.
            lock (_lock)
            {
                outputString = builder.ToString();
            }
            File.AppendAllText(_output, outputString);
            Console.WriteLine("File saved to " + _output);
        }
    }
}
