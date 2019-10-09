using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;


namespace dotnet_counters.Tests
{
    public class CSVExportTest
    {
        private CounterMonitor monitor;

        public CSVExportTest()
        {
            monitor = new CounterMonitor();
        }


        [Fact]
        public void RendererTest()
        {
            JSONExporter exporter = new JSONExporter("testjson", "myProcess");

            exporter.Initialize();
            exporter.CounterPayloadReceived()
        }


        /// <summary>
        /// Generate some ICounterPayloads to be written
        /// </summary>
        private void GeneratePayload()
        {
            IDictionary<string, object> payloadFields;
            payloadFields["Name"] = "someCounter";
            payloadFields["Mean"] = "1";
            payloadFields["Mean"] = ""

            List<ICounterPayload> payloads = new List<ICounterPayload>();
            for (int i = 0; i < 1000; i++)
            {
                payloads.Add(new CounterPayload()) 
            }

        }
    }
}
