// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class LogsPipelineUnitTests
    {
        private const string LoggerRemoteTestName = "LoggerRemoteTest";

        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public LogsPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsPipeline(TestConfiguration config)
        {
            // TODO: When distributed tracing support lands EventPipeTracee
            // gains the ability to start traces. When there is an active trace
            // on .NET9+ logs should automatically receive correlation
            // information (TraceId, SpanId, etc.). Expand this test to validate
            // that once the ActivitySource modifications are present in
            // EventPipeTracee.

            TestLogRecordLogger logger = new();

            await using (TestRunner testRunner = await PipelineTestUtilities.StartProcess(config, LoggerRemoteTestName, _output))
            {
                DiagnosticsClient client = new(testRunner.Pid);

                await using EventLogsPipeline pipeline = new(
                    client,
                    new EventLogsPipelineSettings
                    {
                        Duration = Timeout.InfiniteTimeSpan,
                        FilterSpecs = new Dictionary<string, LogLevel?>()
                        {
                            { LoggerRemoteTestName, LogLevel.Information }
                        }
                    },
                    logger);

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner);
            }

            Assert.Equal(5, logger.LogRecords.Count);

            var logRecordData = logger.LogRecords[0];

            Assert.Equal("Some warning message with 6", logRecordData.LogRecord.FormattedMessage);
            Assert.Equal("Some warning message with {Arg}", logRecordData.LogRecord.MessageTemplate);
            Assert.Equal(LoggerRemoteTestName, logRecordData.LogRecord.CategoryName);
            Assert.Equal(LogLevel.Information, logRecordData.LogRecord.LogLevel);
            Assert.Equal(0, logRecordData.LogRecord.EventId.Id);
            Assert.Single(logRecordData.Attributes);
            Assert.Equal(new("Arg", "6"), logRecordData.Attributes[0]);
            Assert.Single(logRecordData.Scopes);
            Assert.Equal(new("IntValue", "5"), logRecordData.Scopes[0][0]);
            Assert.Equal(new("BoolValue", "true"), logRecordData.Scopes[0][1]);
            Assert.Equal(new("StringValue", "test"), logRecordData.Scopes[0][2]);

            logRecordData = logger.LogRecords[1];

            Assert.Equal("Some other message with 7", logRecordData.LogRecord.FormattedMessage);
            Assert.Equal("Some other message with {Arg}", logRecordData.LogRecord.MessageTemplate);
            Assert.Equal(LoggerRemoteTestName, logRecordData.LogRecord.CategoryName);
            Assert.Equal(LogLevel.Information, logRecordData.LogRecord.LogLevel);
            Assert.Equal(0, logRecordData.LogRecord.EventId.Id);
            Assert.Single(logRecordData.Attributes);
            Assert.Equal(new("Arg", "7"), logRecordData.Attributes[0]);
            Assert.Single(logRecordData.Scopes);
            Assert.Equal(new("IntValue", "6"), logRecordData.Scopes[0][0]);
            Assert.Equal(new("BoolValue", "false"), logRecordData.Scopes[0][1]);
            Assert.Equal(new("StringValue", "string"), logRecordData.Scopes[0][2]);

            logRecordData = logger.LogRecords[2];

            Assert.Equal("Another message", logRecordData.LogRecord.FormattedMessage);
            Assert.Null(logRecordData.LogRecord.MessageTemplate);
            Assert.Equal(LoggerRemoteTestName, logRecordData.LogRecord.CategoryName);
            Assert.Equal(LogLevel.Warning, logRecordData.LogRecord.LogLevel);
            Assert.Equal(7, logRecordData.LogRecord.EventId.Id);
            Assert.Equal("AnotherEventId", logRecordData.LogRecord.EventId.Name);
            Assert.Empty(logRecordData.Attributes);
            Assert.Empty(logRecordData.Scopes);
        }

        private sealed class TestLogRecordLogger : ILogRecordLogger
        {
            public List<(LogRecord LogRecord, KeyValuePair<string, object?>[] Attributes, List<KeyValuePair<string, object?>[]> Scopes)> LogRecords { get; } = new();

            public void Log(
                in LogRecord log,
                ReadOnlySpan<KeyValuePair<string, object?>> attributes,
                in LogRecordScopeContainer scopes)
            {
                List<KeyValuePair<string, object?>[]> scopeCopy = new();

                scopes.ForEachScope(ScopeCallback, ref scopeCopy);

                LogRecords.Add((log, attributes.ToArray(), scopeCopy));

                static void ScopeCallback(ReadOnlySpan<KeyValuePair<string, object?>> attributes, ref List<KeyValuePair<string, object?>[]> state)
                {
                    state.Add(attributes.ToArray());
                }
            }

            public Task PipelineStarted(CancellationToken token) => Task.CompletedTask;

            public Task PipelineStopped(CancellationToken token) => Task.CompletedTask;
        }
    }
}
