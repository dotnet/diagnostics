// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;

namespace Microsoft.Diagnostics.Tools.Counters
{
    /// <summary>
    /// These test the some of the known providers that we provide as a default configuration for customers to use.
    /// </summary>
    public class CSVExporterTests
    {
        [Fact]
        public void IncrementingCounterTest()
        {
            string fileName = "IncrementingCounterTest.csv";
        	CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived("myProvider", GenerateCounterPayload(true, "incrementingCounterOne", 1.0, 1, "Incrementing Counter One"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            int lineCount = 0;
            foreach(string line in File.ReadLines(fileName))
            {
                lineCount += 1;
                string[] tokens = line.Split(',');
                Assert.True("myProvider" == tokens[1] || "Provider" == tokens[1]);
                Assert.True("Incrementing Counter One / 1 sec" == tokens[2] || "Counter Name" == tokens[2]);
                Assert.True("Rate" == tokens[3] || "Counter Type" == tokens[3]);
                Assert.True("1" == tokens[4] || "Mean/Increment" == tokens[4]);
            }
            Assert.Equal(11, lineCount);

            File.Delete(fileName);
        }

        [Fact]
        public void CounterTest()
        {
            string fileName = "CounterTest.csv";
            CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived("myProvider", GenerateCounterPayload(false, "counterOne", 1.0, 1, "Counter One"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            int lineCount = 0;
            foreach (string line in File.ReadLines(fileName))
            {
                lineCount += 1;
                string[] tokens = line.Split(',');

                Assert.True("myProvider" == tokens[1] || "Provider" == tokens[1]);
                Assert.True("Counter One" == tokens[2] || "Counter Name" == tokens[2]);
                Assert.True("Metric" == tokens[3] || "Counter Type" == tokens[3]);
                Assert.True("1" == tokens[4] || "Mean/Increment" == tokens[4]);
            }
            File.Delete(fileName);
        }

        private ICounterPayload GenerateCounterPayload(
            bool isIncrementingCounter,
            string counterName,
            double counterValue,
            int displayRateTimeScaleSeconds=0,
            string displayName="")
        {
            if (isIncrementingCounter)
            {
                Dictionary<string, object> payloadFields = new Dictionary<string, object>();
                payloadFields["Name"] = counterName;
                payloadFields["Increment"] = counterValue;
                payloadFields["DisplayName"] = displayName;
                payloadFields["DisplayRateTimeScale"] = displayRateTimeScaleSeconds == 0 ? "" : TimeSpan.FromSeconds(displayRateTimeScaleSeconds).ToString();
                ICounterPayload payload = new IncrementingCounterPayload(payloadFields, 1);
                return payload;
            }
            else
            {
                Dictionary<string, object> payloadFields = new Dictionary<string, object>();
                payloadFields["Name"] = counterName;
                payloadFields["Mean"] = counterValue;
                payloadFields["DisplayName"] = displayName;
                ICounterPayload payload = new CounterPayload(payloadFields);
                return payload;
            }
        }
    }
}
