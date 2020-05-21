// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Logging;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace DotnetMonitor.UnitTests
{
    public class DiagnosticsMonitorTests
    {
        private readonly ITestOutputHelper _output;

        public DiagnosticsMonitorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(Skip = "Disable test due to errors on MacOS")]
        public async Task TestDiagnosticsEventPipeProcessorLogs()
        {
            var outputStream = new MemoryStream();

            await using (var testExecution = RemoteTestExecution.StartRemoteProcess("LoggerRemoteTest", _output))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                var loggerFactory = new LoggerFactory(new[] { new StreamingLoggerProvider(outputStream) });

                DiagnosticsEventPipeProcessor diagnosticsEventPipeProcessor = new DiagnosticsEventPipeProcessor(
                    ContextConfiguration,
                    PipeMode.Logs,
                    loggerFactory,
                    Enumerable.Empty<IMetricsLogger>());

                var processingTask = diagnosticsEventPipeProcessor.Process(testExecution.TestRunner.Pid, TimeSpan.FromSeconds(10), CancellationToken.None);

                //Add a small delay to make sure diagnostic processor had a chance to initialize
                await Task.Delay(1000);

                //Send signal to proceed with event collection
                testExecution.Start();

                await processingTask;
                await diagnosticsEventPipeProcessor.DisposeAsync();
                loggerFactory.Dispose();
            }

            outputStream.Position = 0L;

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            string firstMessage = reader.ReadLine();
            Assert.NotNull(firstMessage);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(firstMessage);
            Assert.Equal("Some warning message with 6", result.Message);
            Assert.Equal("LoggerRemoteTest", result.Category);
            Assert.Equal("Warning", result.LogLevel);
            Assert.Equal("0", result.EventId);
            Validate(result.Scopes, ("BoolValue", "true"), ("StringValue", "test"), ("IntValue", "5"));
            Validate(result.Arguments, ("arg", "6"));

            string secondMessage = reader.ReadLine();
            Assert.NotNull(secondMessage);

            result = JsonSerializer.Deserialize<LoggerTestResult>(secondMessage);
            Assert.Equal("Another message", result.Message);
            Assert.Equal("LoggerRemoteTest", result.Category);
            Assert.Equal("Warning", result.LogLevel);
            Assert.Equal("0", result.EventId);
            Assert.Equal(0, result.Scopes.Count);
            //We are expecting only the original format
            Assert.Equal(1, result.Arguments.Count);
        }

        private ContextConfiguration ContextConfiguration => new ContextConfiguration { Namespace = "default", Node = Environment.MachineName };

        private static void Validate(IDictionary<string, JsonElement> values, params (string key, object value)[] expectedValues)
        {
            Assert.NotNull(values);
            foreach(var expectedValue in expectedValues)
            {
                Assert.True(values.TryGetValue(expectedValue.key, out JsonElement value));
                //TODO For now this will always be a string
                Assert.Equal(expectedValue.value, value.GetString());
            }
        }

        private sealed class LoggerTestResult
        {
            public string Category { get; set; }
            public string LogLevel { get; set; }
            public string EventId { get; set; }
            public string Message { get; set; }
            public IDictionary<string, JsonElement> Arguments { get; set; }
            public IDictionary<string, JsonElement> Scopes { get; set; }
        }
    }
}
