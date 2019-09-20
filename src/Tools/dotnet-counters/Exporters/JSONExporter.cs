using System;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    class JSONExporter : ICounterExporter
    {
        private string processName;
        private string output;
        private StringBuilder builder;
        private int flushLength = 10_000; // Arbitrary length to flush

        public void Initialize(string _output, string _processName)
        {
            output = _output + ".json";
            processName = _processName;
            builder = new StringBuilder();
            builder.Append($"{{ \"Target Process\": \"{processName}\", ");
            builder.Append($"\"Start Time\": \"{DateTime.Now.ToString()}\", ");
            builder.Append($"\"Events\": [");
        }
        public void Write(string providerName, ICounterPayload counterPayload)
        {
            if (builder.Length > flushLength)
            {
                File.AppendAllText(output, builder.ToString());
                builder.Clear();
            }
            builder.Append($"{{ \"timestamp\": \"{DateTime.Now.ToString()}\", ");
            builder.Append($" \"provider\": \"{providerName}\", ");
            builder.Append($" \"name\": \"{counterPayload.GetDisplay()}\", ");
            builder.Append($" \"value\": {counterPayload.GetValue()} }},");
        }

        public void Flush()
        {
            builder.Append($"] }}");
            // Append all the remaining text to the file.
            File.AppendAllText(output, builder.ToString());
        }
    }
}
