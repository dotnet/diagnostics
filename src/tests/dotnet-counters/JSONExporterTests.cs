// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Newtonsoft.Json;
using Microsoft.Diagnostics.Tools.Counters;

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
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload("myProvider", "incrementingCounterOne", "Incrementing Counter One", "", "", 1, 1, start + TimeSpan.FromSeconds(i)), false);
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
                    Assert.Equal("Incrementing Counter One (Count / 1 sec)", payload.name);
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
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload("myProvider", "counterOne", "Counter One", "", "", 1, start + TimeSpan.FromSeconds(i)), false);
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
        public void DisplayUnitsTest()
        {
            string fileName = "displayUnitsTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0 ; i < 20; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload("myProvider", "heapSize", "Heap Size", "MB", "", i, start + TimeSpan.FromSeconds(i)), false);
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
            DateTime start = DateTime.Now;
            for (int i = 0 ; i < 20; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload("myProvider", "heapSize", "Heap Size", "MB", "", 0, 60, start + TimeSpan.FromSeconds(i)), false);
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

        [Fact]
        public void TagsTest()
        {
            string fileName = "TagsTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload("myProvider", "counterOne", "Counter One", "", "f=abc,two=9", 1, start + TimeSpan.FromSeconds(i)), false);
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
                    Assert.Equal("f=abc,two=9", payload.tags);
                }
            }
        }

        [Fact]
        public void EscapingTest()
        {
            string fileName = "EscapingTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload("myProvider\\", "counterOne\f", "CounterOne\f", "", "f\b\"\n=abc\r\\,\ttwo=9", 1, start + TimeSpan.FromSeconds(i)), false);
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
                    Assert.Equal("myProvider\\", payload.provider);
                    Assert.Equal("CounterOne\f", payload.name);
                    Assert.Equal("Metric", payload.counterType);
                    Assert.Equal(1.0, payload.value);
                    Assert.Equal("f\b\"\n=abc\r\\,\ttwo=9", payload.tags);
                }
            }
        }

        [Fact]
        public void PercentilesTest()
        {
            string fileName = "PercentilesTest.json";
            JSONExporter exporter = new JSONExporter(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new PercentilePayload("myProvider", "counterOne", "Counter One", "", "f=abc,Percentile=50", 1, start + TimeSpan.FromSeconds(i)), false);
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
                    Assert.Equal("f=abc,Percentile=50", payload.tags);
                }
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

        [JsonProperty("tags")]
        public string tags { get; set; }

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
