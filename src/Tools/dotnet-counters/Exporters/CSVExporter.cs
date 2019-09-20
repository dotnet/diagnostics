using System;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    class CSVExporter : ICounterExporter
    {
        private string output;
        private StringBuilder builder;
        private int flushLength = 10_000; // Arbitrary length to flush

        public void Initialize(string _output, string processName)
        {
            output = _output + ".csv";

            if (File.Exists(output))
            {
                Console.WriteLine($"[Warning] {output} already exists. This file will be overwritten.");
                File.Delete(output);
            }
            builder = new StringBuilder();
            builder.AppendLine("Timestamp,Provider,Counter Name,Counter Type,Mean/Increment");
        }

        public void Write(string providerName, ICounterPayload counterPayload)
        {
            if (builder.Length > flushLength)
            {
                File.AppendAllText(output, builder.ToString());
                builder.Clear();
            }
            builder.Append(DateTime.UtcNow.ToString() + ",");
            builder.Append(providerName + ",");
            builder.Append(counterPayload.GetDisplay() + ",");
            builder.Append(counterPayload.GetCounterType()+",");
            builder.Append(counterPayload.GetValue() + "\n");
        }

        public void Flush()
        {
            // Append all the remaining text to the file.
            File.AppendAllText(output, builder.ToString());
        }
    }
}
