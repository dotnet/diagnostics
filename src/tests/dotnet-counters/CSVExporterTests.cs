// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;
using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;

namespace DotnetCounters.UnitTests
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
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload("myProvider", "incrementingCounterOne", "Incrementing Counter One", "", "", i, 1, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                string[] headerTokens = lines[0].Split(',');
                Assert.Equal("Provider", headerTokens[1]);
                Assert.Equal("Counter Name", headerTokens[2]);
                Assert.Equal("Counter Type", headerTokens[3]);
                Assert.Equal("Mean/Increment", headerTokens[4]);

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

        [Fact]
        public void CounterTest()
        {
            string fileName = "CounterTest.csv";
            CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 10; i++)
            {
                exporter.CounterPayloadReceived(new GaugePayload("myProvider", "counterOne", "Counter One", "", "", i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(11, lines.Count); // should be 11 including the headers

                string[] headerTokens = lines[0].Split(',');
                Assert.Equal("Provider", headerTokens[1]);
                Assert.Equal("Counter Name", headerTokens[2]);
                Assert.Equal("Counter Type", headerTokens[3]);
                Assert.Equal("Mean/Increment", headerTokens[4]);


                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal("Counter One", tokens[2]);
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
        public void DifferentDisplayRateTest()
        {
            string fileName = "displayRateTest.csv";
        	CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload("myProvider", "incrementingCounterOne", "Incrementing Counter One", "", "", i, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                string[] headerTokens = lines[0].Split(',');
                Assert.Equal("Provider", headerTokens[1]);
                Assert.Equal("Counter Name", headerTokens[2]);
                Assert.Equal("Counter Type", headerTokens[3]);
                Assert.Equal("Mean/Increment", headerTokens[4]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Incrementing Counter One (Count / 60 sec)", tokens[2]);
                    Assert.Equal("Rate", tokens[3]);
                    Assert.Equal((i-1).ToString(), tokens[4]);
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
            CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload("myProvider", "allocRateGen", "Allocation Rate Gen", "MB", "", i, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                string[] headerTokens = lines[0].Split(',');
                Assert.Equal("Provider", headerTokens[1]);
                Assert.Equal("Counter Name", headerTokens[2]);
                Assert.Equal("Counter Type", headerTokens[3]);
                Assert.Equal("Mean/Increment", headerTokens[4]);

                for (int i = 1; i < lines.Count; i++)
                {
                    string[] tokens = lines[i].Split(',');

                    Assert.Equal("myProvider", tokens[1]);
                    Assert.Equal($"Allocation Rate Gen (MB / 60 sec)", tokens[2]);
                    Assert.Equal("Rate", tokens[3]);
                    Assert.Equal((i-1).ToString(), tokens[4]);
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
            CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new RatePayload("myProvider", "allocRateGen", "Allocation Rate Gen", "MB", "foo=bar,baz=7", i, 60, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                string[] headerTokens = lines[0].Split(',');
                Assert.Equal("Provider", headerTokens[1]);
                Assert.Equal("Counter Name", headerTokens[2]);
                Assert.Equal("Counter Type", headerTokens[3]);
                Assert.Equal("Mean/Increment", headerTokens[4]);

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
            CSVExporter exporter = new CSVExporter(fileName);
            exporter.Initialize();
            DateTime start = DateTime.Now;
            for (int i = 0; i < 100; i++)
            {
                exporter.CounterPayloadReceived(new PercentilePayload("myProvider", "allocRateGen", "Allocation Rate Gen", "MB", "foo=bar,Percentile=50", i, start + TimeSpan.FromSeconds(i)), false);
            }
            exporter.Stop();

            Assert.True(File.Exists(fileName));

            try
            {
                List<string> lines = File.ReadLines(fileName).ToList();
                Assert.Equal(101, lines.Count); // should be 101 including the headers

                string[] headerTokens = lines[0].Split(',');
                Assert.Equal("Provider", headerTokens[1]);
                Assert.Equal("Counter Name", headerTokens[2]);
                Assert.Equal("Counter Type", headerTokens[3]);
                Assert.Equal("Mean/Increment", headerTokens[4]);

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
    }
}
