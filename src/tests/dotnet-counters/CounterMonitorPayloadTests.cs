// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommonTestRunner;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.Tools;
using Microsoft.Diagnostics.Tools.Counters;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsTCPIP;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using Xunit.Sdk;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace DotnetCounters.UnitTests
{
    /// <summary>
    /// These test the various internal logic in CounterMonitor
    /// </summary>
    public class CounterMonitorPayloadTests
    {
        private ITestOutputHelper _outputHelper;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

        public CounterMonitorPayloadTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestCounterMonitor(TestConfiguration configuration)
        {
            if (configuration.BuildProjectFramework != "net8.0")
            {
                throw new SkipTestException("Inapplicable framework");
            }

            CounterMonitor monitor = new CounterMonitor();
            string path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "json");

            try
            {
                using CancellationTokenSource source = new CancellationTokenSource(DefaultTimeout);

                await using var testRunner = await TestRunnerUtilities.StartProcess(configuration, "TestCounterMonitor DiagMetrics", _outputHelper);

                await TestRunnerUtilities.ExecuteCollection((ct) => {
                    return Task.Run(async () =>
                        await monitor.Collect(
                            ct: ct,
                            counter_list: new List<string> { "System.Runtime", "TestMeter" },
                            counters: null,
                            console: new TestConsole(),
                            processId: testRunner.Pid,
                            refreshInterval: 1,
                            format: CountersExportFormat.json,
                            output: path,
                            name: null,
                            diagnosticPort: null,
                            resumeRuntime: false,
                            maxHistograms: 10,
                            maxTimeSeries: 10,
                            duration: TimeSpan.FromSeconds(10)));
                }, testRunner, source.Token);

                using FileStream metricsFile = File.OpenRead(path);

                JSONCounterTrace trace = JsonSerializer.Deserialize<JSONCounterTrace>(metricsFile, new JsonSerializerOptions { PropertyNameCaseInsensitive = true});

                return;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch { }
            }
        }

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        private sealed class TestConsole : IConsole
        {
            private readonly TestStandardStreamWriter _outWriter;
            private readonly TestStandardStreamWriter _errorWriter;

            private sealed class TestStandardStreamWriter : IStandardStreamWriter
            {
                private StringWriter _writer = new();
                public void Write(string value) => _writer.Write(value);
                public void WriteLine(string value) => _writer.WriteLine(value);
            }

            public TestConsole()
            {
                _outWriter = new TestStandardStreamWriter();
                _errorWriter = new TestStandardStreamWriter();
            }

            public IStandardStreamWriter Out => _outWriter;

            public bool IsOutputRedirected => true;

            public IStandardStreamWriter Error => _errorWriter;

            public bool IsErrorRedirected => true;

            public bool IsInputRedirected => false;
        }
    }
}
