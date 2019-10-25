// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    class CSVExporter : ICounterRenderer
    {
        private string _output;
        private StringBuilder builder;
        private int flushLength = 10_000; // Arbitrary length to flush

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
            builder = new StringBuilder();
            builder.AppendLine("Timestamp,Provider,Counter Name,Counter Type,Mean/Increment");
        }

        public void EventPipeSourceConnected()
        {
            Console.WriteLine("Starting a counter session. Press Q to quit.");
        }

        public void ToggleStatus(bool paused)
        {
            // Do nothing
        }

        public void CounterPayloadReceived(string providerName, ICounterPayload payload, bool _)
        {
            if (builder.Length > flushLength)
            {
                File.AppendAllText(_output, builder.ToString());
                builder.Clear();
            }
            builder.Append(DateTime.UtcNow.ToString() + ",");
            builder.Append(providerName + ",");
            builder.Append(payload.GetDisplay() + ",");
            builder.Append(payload.GetCounterType() + ",");
            builder.Append(payload.GetValue() + "\n");
        }

        public void Stop()
        {
            // Append all the remaining text to the file.
            File.AppendAllText(_output, builder.ToString());
            Console.WriteLine("File saved to " + _output);
        }
    }
}
