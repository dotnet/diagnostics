// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventLogsPipelineUnitTests
    {
        private readonly ITestOutputHelper _output;

        public EventLogsPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task TestLogs()
        {
            var outputStream = new MemoryStream();

            await using (var testExecution = StartTraceeProcess("LoggerRemoteTest"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                using var loggerFactory = new LoggerFactory(new[] { new TestStreamingLoggerProvider(outputStream) });
                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);

                var logSettings = new EventLogsPipelineSettings { Duration = Timeout.InfiniteTimeSpan};
                await using var pipeline = new EventLogsPipeline(client, logSettings, loggerFactory);

                await PipelineTestUtilities.ExecutePipelineWithDebugee(pipeline, testExecution);
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

        private RemoteTestExecution StartTraceeProcess(string loggerCategory)
        {
            return RemoteTestExecution.StartProcess(CommonHelper.GetTraceePathWithArgs("EventPipeTracee") + " " + loggerCategory, _output);
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
