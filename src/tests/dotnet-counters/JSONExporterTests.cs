// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Newtonsoft.Json;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the some of the known providers that we provide as a default configuration for customers to use.
    /// </summary>
    public class JSONExporterTests
    {
        [Fact]
        public void IncrementingCounterTest()
        {
            string fileName = "IncrementingCounterTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived("myProvider", TestHelpers.GenerateCounterPayload(true, "incrementingCounterOne", 1.0, 1, "Incrementing Counter One"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new StreamReader(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);

                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(10, counterTrace.events.Length);
                foreach (JSONCounterPayload payload in counterTrace.events)
                {
                    Assert.Equal("myProvider", payload.provider);
                    Assert.Equal("Incrementing Counter One / 1 sec", payload.name);
                    Assert.Equal("Rate", payload.counterType);
                    Assert.Equal(1.0, payload.value);
                }
            }
        }

        [Fact]
        public void CounterTest()
        {
            string fileName = "CounterTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived("myProvider", TestHelpers.GenerateCounterPayload(false, "counterOne", 1.0, 1, "Counter One"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new StreamReader(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);

                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(10, counterTrace.events.Length);
                foreach (JSONCounterPayload payload in counterTrace.events)
                {
                    Assert.Equal("myProvider", payload.provider);
                    Assert.Equal("Counter One", payload.name);
                    Assert.Equal("Metric", payload.counterType);
                    Assert.Equal(1.0, payload.value);
                }
            }
        }

        [Fact]
        public void DifferentDisplayRateTest()
        {
            string fileName = "displayRateTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived("myProvider", TestHelpers.GenerateCounterPayload(true, "incrementingCounterOne", 1.0, 60, "Incrementing Counter One"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new StreamReader(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);

                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(10, counterTrace.events.Length);
                foreach (JSONCounterPayload payload in counterTrace.events)
                {
                    Assert.Equal("myProvider", payload.provider);
                    Assert.Equal("Incrementing Counter One / 60 sec", payload.name);
                    Assert.Equal("Rate", payload.counterType);
                    Assert.Equal(60.0, payload.value);
                }
            }
        }

        [Fact]
        public void DisplayUnitsTest()
        {
            string fileName = "displayUnitsTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();

            for (int i = 0 ; i < 20; i++)
            {
                exporter.CounterPayloadReceived("myProvider", TestHelpers.GenerateCounterPayload(false, "heapSize", (double)i, 0, "Heap Size", "MB"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new StreamReader(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);
                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(20, counterTrace.events.Length);
                var i = 0;
                foreach(JSONCounterPayload payload in counterTrace.events)
                {
                    Assert.Equal("myProvider", payload.provider);
                    Assert.Equal("Heap Size (MB)", payload.name);
                    Assert.Equal(i, payload.value);
                    i += 1;
                }
            }
        }

        [Fact]
        public void ValidJSONFormatTest()
        {
            // Test if the produced JSON is a valid format. 
            // Regression test for https://github.com/dotnet/diagnostics/issues/1020

            string fileName = "validJSONFormatTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();

            for (int i = 0 ; i < 20; i++)
            {
                exporter.CounterPayloadReceived("myProvider", TestHelpers.GenerateCounterPayload(false, "heapSize", 0, 0, "Heap Size", "MB"), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new StreamReader(fileName))
            {
                string json = r.ReadToEnd();
                // first } from end of the last event payload
                // next ] from closing "Events" field 
                // last } from closing the whole JSON
                Assert.EndsWith("0 }]}", json);
            }
        }
    }

    class JSONCounterPayload
    {
        [JsonProperty("timestamp")]
        public string timestamp { get; set; }

        [JsonProperty("provider")]
        public string provider { get; set; }

        [JsonProperty("name")]
        public string name { get; set; }

        [JsonProperty("counterType")]
        public string counterType { get; set; }

        [JsonProperty("value")]
        public double value { get; set; }
    }

    class JSONCounterTrace
    {
        [JsonProperty("TargetProcess")]
        public string targetProcess { get; set; }

        [JsonProperty("StartTime")]
        public string startTime { get; set; }

        [JsonProperty("Events")]
        public JSONCounterPayload[] events { get; set; }
    }
}
