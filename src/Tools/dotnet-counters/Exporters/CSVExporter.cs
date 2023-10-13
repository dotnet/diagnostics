// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

                builder
                    .Append(payload.Timestamp.ToString()).Append(',')
                    .Append(payload.Provider.ProviderName).Append(',')
                    .Append(payload.GetDisplay());

                string tags = payload.CombineTags();
                if (!string.IsNullOrEmpty(tags))
                {
                    builder.Append('[').Append(tags.Replace(',', ';')).Append(']');
                }
                builder.Append(',')
                    .Append(payload.CounterType).Append(',')
                    .Append(payload.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        public void CounterStopped(CounterPayload payload) { }

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
