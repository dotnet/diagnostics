// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

        [SkippableFact]
        public async Task TestLogs()
        {

            var outputStream = new MemoryStream();

            await using (var testExecution = StartTraceeProcess("LoggerRemoteTest"))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                using var loggerFactory = new LoggerFactory(new[] { new TestStreamingLoggerProvider(outputStream) });
                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);

                var logSettings = new EventLogsPipelineSettings { Duration = Timeout.InfiniteTimeSpan };
                await using var pipeline = new EventLogsPipeline(client, logSettings, loggerFactory);

                await PipelineTestUtilities.ExecutePipelineWithDebugee(pipeline, testExecution);
            }

            outputStream.Position = 0L;

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            string firstMessage = reader.ReadLine();
            _output.WriteLine("[Test] First message: {0}", firstMessage);
            Assert.NotNull(firstMessage);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(firstMessage);
            Assert.Equal("Some warning message with 6", result.Message);
            Assert.Equal("LoggerRemoteTest", result.Category);
            Assert.Equal("Warning", result.LogLevel);
            Assert.Equal("0", result.EventId);
            ValidateScopes(result, ("BoolValue", "true"), ("StringValue", "test"), ("IntValue", "5"));
            ValidateArguments(result, ("arg", "6"));

            string secondMessage = reader.ReadLine();
            _output.WriteLine("[Test] Second message: {0}", secondMessage);
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

        private static void ValidateScopes(LoggerTestResult result, params (string key, string value)[] expectedValues)
        {
            Validate(nameof(LoggerTestResult.Scopes), result.Scopes, expectedValues);
        }

        private static void ValidateArguments(LoggerTestResult result, params (string key, string value)[] expectedValues)
        {
            Validate(nameof(LoggerTestResult.Arguments), result.Arguments, expectedValues);
        }

        private static void Validate(string sourceName, IDictionary<string, JsonElement> values, params (string key, string value)[] expectedValues)
        {
            AssertX.NotNull(values, $"Expected {sourceName} to not be null.");
            foreach(var expectedValue in expectedValues)
            {
                Assert.True(values.TryGetValue(expectedValue.key, out JsonElement value), $"Unable to find {expectedValue.key} key in {sourceName}.");
                //TODO For now this will always be a string
                AssertX.Equal(expectedValue.value, value.GetString(), $"Expected {expectedValue.key} key to have different value in {sourceName}.");
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
