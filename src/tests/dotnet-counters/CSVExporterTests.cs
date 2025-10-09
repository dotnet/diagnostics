// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Xunit;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the some of the known providers that we provide as a default configuration for customers to use.
    /// </summary>
    public class CSVExporterTests
    {
        private const string tag1 = "foo=bar";
        private const string tag2 = "baz=7";
        private const string otherTag1 = "foo2=bar2";
        private const string otherTag2 = "baz2=8";
        private const string meterTag1 = "MeterTagKey=MeterTagValue";
        private const string meterTag2 = "MeterTagKey2=MeterTagValue2";
        private const string instrumentTag1 = "InstrumentTagKey=InstrumentTagValue";
        private const string instrumentTag2 = "InstrumentTagKey2=InstrumentTagValue2";
        private const string otherInstrumentTag1 = "OtherInstrumentTagKey=OtherInstrumentTagValue";
        private const string otherInstrumentTag2 = "OtherInstrumentTagKey2=OtherInstrumentTagValue2";

        [Fact]
        public void IncrementingCounterTest()
        {
            string fileName = "IncrementingCounterTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload(new CounterMetadata("myProvider", "incrementingCounterOne", counterUnit: string.Empty), "Incrementing Counter One", string.Empty, string.Empty, i, 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Incrementing Counter One (Count / 1 sec)", tokens[2]);
                    Assert.Equal("Rate", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Theory]
        [InlineData("", "", "", "")]
        [InlineData($"{meterTag1},{meterTag2}", "", "", $"[{meterTag1};{meterTag2}]")]
        [InlineData("", $"{instrumentTag1},{instrumentTag2}", "", $"[{instrumentTag1};{instrumentTag2}]")]
        [InlineData($"{meterTag1},{meterTag2}", $"{instrumentTag1},{instrumentTag2}", "", $"[{meterTag1};{meterTag2};{instrumentTag1};{instrumentTag2}]")]
        [InlineData($"{meterTag1},{meterTag2}", $"{instrumentTag1},{instrumentTag2}", $"{tag1},{tag2}", $"[{meterTag1};{meterTag2};{instrumentTag1};{instrumentTag2};{tag1};{tag2}]")]
        public void CounterTest(string meterTags, string instrumentTags, string tags, string expectedTags)
        {
            string fileName = "CounterTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(
                    new GaugePayload(
                        new CounterMetadata("myProvider", "counterOne", meterTags, instrumentTags), "Counter One", string.Empty, tags, i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(11, lines.Count); // should be 11 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Counter One{expectedTags}", tokens[2]);
                    Assert.Equal("Metric", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        // Starting in .NET 8 MetricsEventSource, Meter counter instruments report both rate of change and
        // absolute value. Reporting rate in the UI was less useful for many counters than just seeing the raw
        // value. Now dotnet-counters reports these counters as absolute values.
        [Fact]
        public void CounterReportsAbsoluteValuePostNet8()
        {
            string fileName = "CounterReportsAbsoluteValuePostNet8.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new CounterRateAndValuePayload(new CounterMetadata("myProvider", "counter", counterUnit: string.Empty), "Counter One", string.Empty, string.Empty, rate: 0, i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Counter One (Count)", tokens[2]);
                    Assert.Equal("Metric", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Fact]
        public void CounterTest_SameMeterDifferentTagsPerInstrument()
        {
            string fileName = "CounterTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;

            exporter.CounterPayloadReceived(new GaugePayload(
                new CounterMetadata("myProvider", "counterOne", $"{meterTag1},{meterTag2}", $"{instrumentTag1},{instrumentTag2}"), "Counter One", string.Empty, $"{tag1},{tag2}", 0, start + TimeSpan.FromSeconds(0)), false);
            exporter.CounterPayloadReceived(new GaugePayload(
                new CounterMetadata("myProvider", "counterTwo", $"{meterTag1},{meterTag2}", $"{otherInstrumentTag1},{otherInstrumentTag2}"), "Counter Two", string.Empty, $"{otherTag1},{otherTag2}", 1, start + TimeSpan.FromSeconds(1)), false);

            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(3, lines.Count); // should be 3 including the headers

                ValidateHeaderTokens(lines[0]);

                string[] tokens1 = lines[1].Split(',');
                string expectedTags1 = $"[{meterTag1};{meterTag2};{instrumentTag1};{instrumentTag2};{tag1};{tag2}]";

                Assert.Equal("myProvider", tokens1[1]);
                Assert.Equal($"Counter One{expectedTags1}", tokens1[2]);
                Assert.Equal("Metric", tokens1[3]);
                Assert.Equal(0.ToString(), tokens1[4]);

                string[] tokens2 = lines[2].Split(',');
                string expectedTags2 = $"[{meterTag1};{meterTag2};{otherInstrumentTag1};{otherInstrumentTag2};{otherTag1};{otherTag2}]";

                Assert.Equal("myProvider", tokens2[1]);
                Assert.Equal($"Counter Two{expectedTags2}", tokens2[2]);
                Assert.Equal("Metric", tokens2[3]);
                Assert.Equal(1.ToString(), tokens2[4]);
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Fact]
        public void DifferentDisplayRateTest()
        {
            string fileName = "displayRateTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload(new CounterMetadata("myProvider", "incrementingCounterOne", counterUnit: string.Empty), "Incrementing Counter One", string.Empty, null, i, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Incrementing Counter One (Count / 60 sec)", tokens[2]);
                    Assert.Equal("Rate", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Fact]
        public void DisplayUnitsTest()
        {
            string fileName = "displayUnitsTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload(new CounterMetadata("myProvider", "allocRateGen", "MB"), "Allocation Rate Gen", string.Empty, string.Empty, i, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Allocation Rate Gen (MB / 60 sec)", tokens[2]);
                    Assert.Equal("Rate", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Fact]
        public void TagsTest()
        {
            string fileName = "tagsTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload(new CounterMetadata("myProvider", "allocRateGen", "MB"), "Allocation Rate Gen", string.Empty, "foo=bar,baz=7", i, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Allocation Rate Gen (MB / 60 sec)[foo=bar;baz=7]", tokens[2]);
                    Assert.Equal("Rate", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        [Fact]
        public void PercentilesTest()
        {
            string fileName = "percentilesTest.csv";
            CSVExporter exporter = new(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new PercentilePayload(new CounterMetadata("myProvider", "allocRateGen", "MB"), "Allocation Rate Gen", string.Empty, "foo=bar,Percentile=50", i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                ValidateHeaderTokens(lines[0]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Allocation Rate Gen (MB)[foo=bar;Percentile=50]", tokens[2]);
                    Assert.Equal("Metric", tokens[3]);
                    Assert.Equal((i - 1).ToString(), tokens[4]);
                }
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        internal static void ValidateHeaderTokens(string headerLine)
        {
            string[] headerTokens = headerLine.Split(',');
            Assert.Equal("Provider", headerTokens[TestConstants.ProviderIndex]);
            Assert.Equal("Counter Name", headerTokens[TestConstants.CounterNameIndex]);
            Assert.Equal("Counter Type", headerTokens[TestConstants.CounterTypeIndex]);
            Assert.Equal("Mean/Increment", headerTokens[TestConstants.ValueIndex]);
        }
    }
}
