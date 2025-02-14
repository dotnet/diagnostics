// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Newtonsoft.Json;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Xunit;
using System.Collections.Generic;

#pragma warning disable CA1507 // Use nameof to express symbol names

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
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload(new CounterMetadata("myProvider", "incrementingCounterOne", counterUnit: string.Empty), "Incrementing Counter One", string.Empty, string.Empty, 1, 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider", "counterOne", counterUnit: string.Empty), "Counter One", string.Empty, string.Empty, 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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
        public void CounterTest_AllTags()
        {
            string meterTags = "MeterTagKey=MeterTagValue,MeterTagKey2=MeterTagValue2";
            string instrumentTags = "InstrumentTagKey=InstrumentTagValue,InstrumentTagKey2=InstrumentTagValue2";

            string fileName = "CounterTest.json";
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider", "counterOne", meterTags, instrumentTags), "Counter One", string.Empty, "f=abc,two=9", 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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
                    Assert.Equal(meterTags, payload.meterTags);
                    Assert.Equal(instrumentTags, payload.instrumentTags);
                }
            }
        }

        [Fact]
        public void CounterTest_SameMeterDifferentTagsPerInstrument()
        {
            string meterTags = "MeterTagKey=MeterTagValue,MeterTagKey2=MeterTagValue2";
            string instrumentTags = "InstrumentTagKey=InstrumentTagValue,InstrumentTagKey2=InstrumentTagValue2";
            string otherInstrumentTags = "OtherInstrumentTagKey=OtherInstrumentTagValue,OtherInstrumentTagKey2=OtherInstrumentTagValue2";

            string fileName = "CounterTest.json";
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;

            exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider", "counterOne", meterTags, instrumentTags), "Counter One", string.Empty, "f=abc,two=9", 1, start + TimeSpan.FromSeconds(1)), false);
            exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider", "counterTwo", meterTags, otherInstrumentTags), "Counter Two", string.Empty, "g=bcd,three=10", 1, start + TimeSpan.FromSeconds(2)), false);

            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);

                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(2, counterTrace.events.Length);

                JSONCounterPayload payload1 = counterTrace.events[0];
                Assert.Equal("myProvider", payload1.provider);
                Assert.Equal("Counter One", payload1.name);
                Assert.Equal("Metric", payload1.counterType);
                Assert.Equal(1.0, payload1.value);
                Assert.Equal("f=abc,two=9", payload1.tags);
                Assert.Equal(meterTags, payload1.meterTags);
                Assert.Equal(instrumentTags, payload1.instrumentTags);

                JSONCounterPayload payload2 = counterTrace.events[1];
                Assert.Equal("myProvider", payload2.provider);
                Assert.Equal("Counter Two", payload2.name);
                Assert.Equal("Metric", payload2.counterType);
                Assert.Equal(1.0, payload2.value);
                Assert.Equal("g=bcd,three=10", payload2.tags);
                Assert.Equal(meterTags, payload2.meterTags);
                Assert.Equal(otherInstrumentTags, payload2.instrumentTags);
            }
        }

        [Fact]
        public void DisplayUnitsTest()
        {
            string fileName = "displayUnitsTest.json";
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 20; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider", "heapSize", "MB"), "Heap Size", string.Empty, string.Empty, i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);
                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(20, counterTrace.events.Length);
                int i = 0;
                foreach (JSONCounterPayload payload in counterTrace.events)
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
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 20; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload(new CounterMetadata("myProvider", "heapSize", "MB"), "Heap Size", string.Empty, string.Empty, 0, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider", "counterOne", counterUnit: ""), "Counter One", string.Empty, "f=abc,two=9", 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload(new CounterMetadata("myProvider\\", "counterOne\f", counterUnit: ""), "CounterOne\f", string.Empty, "f\b\"\n=abc\r\\,\ttwo=9", 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new PercentilePayload(new CounterMetadata("myProvider", "counterOne", counterUnit: ""), "Counter One", string.Empty, "f=abc,Percentile=50", 1, start + TimeSpan.FromSeconds(1)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
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

        [Fact]
        public void CounterReportsAbsoluteValuePostNet8()
        {
            // Starting in .NET 8 MetricsEventSource, Meter counter instruments report both rate of change and
            // absolute value. Reporting rate in the UI was less useful for many counters than just seeing the raw
            // value. Now dotnet-counters reports these counters as absolute values.

            string fileName = "counterReportsAbsoluteValuePostNet8.json";
            JSONExporter exporter = new(fileName, "myProcess.exe");
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 20; i++)
            {
                exporter.CounterPayloadReceived(new CounterRateAndValuePayload(new CounterMetadata("myProvider", "heapSize", "MB"), "Heap Size", string.Empty, string.Empty, rate: 0, i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));
            using (StreamReader r = new(fileName))
            {
                string json = r.ReadToEnd();
                JSONCounterTrace counterTrace = JsonConvert.DeserializeObject<JSONCounterTrace>(json);
                Assert.Equal("myProcess.exe", counterTrace.targetProcess);
                Assert.Equal(20, counterTrace.events.Length);
                int i = 0;
                foreach (JSONCounterPayload payload in counterTrace.events)
                {
                    Assert.Equal("myProvider", payload.provider);
                    Assert.Equal("Heap Size (MB)", payload.name);
                    Assert.Equal("Metric", payload.counterType);
                    Assert.Equal(i, payload.value);
                    i += 1;
                }
            }
        }
    }

    internal class JSONCounterPayload
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

        [JsonProperty("meterTags")]
        public string meterTags { get; set; }

        [JsonProperty("instrumentTags")]
        public string instrumentTags { get; set; }
    }

    internal class JSONCounterTrace
    {
        [JsonProperty("TargetProcess")]
        public string targetProcess { get; set; }

        [JsonProperty("StartTime")]
        public string startTime { get; set; }

        [JsonProperty("Events")]
        public JSONCounterPayload[] events { get; set; }
    }
}
