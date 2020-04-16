// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    class JSONExporter : ICounterRenderer
    {
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

            builder = new StringBuilder();
            builder.Append($"{{ \"TargetProcess\": \"{_processName}\", ");
            builder.Append($"\"StartTime\": \"{DateTime.Now.ToString()}\", ");
            builder.Append($"\"Events\": [");
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
            builder.Append($"{{ \"timestamp\": \"{DateTime.Now.ToString("u")}\", ");
            builder.Append($" \"provider\": \"{providerName}\", ");
            builder.Append($" \"name\": \"{payload.GetDisplay()}\", ");
            builder.Append($" \"counterType\": \"{payload.GetCounterType()}\", ");
            builder.Append($" \"value\": {payload.GetValue()} }},");
        }

        public void Stop()
        {
            builder.Remove(builder.Length - 1, 1); // Remove the last comma to ensure valid JSON format.
            builder.Append($"]}}");
            // Append all the remaining text to the file.
            File.AppendAllText(_output, builder.ToString());
            Console.WriteLine("File saved to " + _output);
        }
    }
}
