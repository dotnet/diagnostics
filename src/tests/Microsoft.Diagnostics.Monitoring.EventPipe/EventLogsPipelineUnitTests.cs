// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
        private const string WildcardCagetoryName = "*";
        private const string AppLoggerCategoryName = "AppLoggerCategory";
        private const string LoggerRemoteTestName = "LoggerRemoteTest";

        private readonly ITestOutputHelper _output;

        public EventLogsPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Test that all log events are collected if no filters are specified.
        /// </summary>
        [SkippableFact]
        public async Task TestLogsAllCategoriesAllLevels()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(settings =>
            {
                settings.UseAppFilters = false;
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            ValidateLoggerRemoteCategoryInformationMessage(reader);
            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryInformationMessage(reader);
            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events at or above the default level are collected.
        /// </summary>
        [SkippableFact]
        public async Task TestLogsAllCategoriesDefaultLevel()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }
            using Stream outputStream = await GetLogsAsync(settings =>
            {
                settings.UseAppFilters = false;
                settings.LogLevel = LogLevel.Warning;
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events at the default level are collected for categories without a specified level.
        /// </summary>
        [SkippableFact]
        public async Task TestLogsAllCategoriesDefaultLevelFallback()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(settings =>
            {
                settings.UseAppFilters = false;
                settings.LogLevel = LogLevel.Error;
                settings.FilterSpecs = new Dictionary<string, LogLevel?>()
                {
                    { AppLoggerCategoryName, null },
                    { LoggerRemoteTestName, LogLevel.Trace }
                };
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            ValidateLoggerRemoteCategoryInformationMessage(reader);
            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that LogLevel.None is not supported as the default log level.
        /// </summary>
        [Fact]
        public async Task TestLogsAllCategoriesDefaultLevelNoneNotSupported()
        {
            // Pipeline should throw PipelineException with inner exception of NotSupportedException.
            PipelineException exception = await Assert.ThrowsAsync<PipelineException>(
                () => GetLogsAsync(
                    settings =>
                    {
                        settings.UseAppFilters = false;
                        settings.LogLevel = LogLevel.None;
                    }));

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }

        /// <summary>
        /// Test that log events are collected for the categories and levels specified by the application.
        /// </summary>
        [Fact]
        public async Task TestLogsUseAppFilters()
        {
            using Stream outputStream = await GetLogsAsync();

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events are collected for the categories and levels specified by the application
        /// and for the categories and levels specified in the filter specs.
        /// </summary>
        [SkippableFact]
        public async Task TestLogsUseAppFiltersAndFilterSpecs()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(settings =>
            {
                settings.FilterSpecs = new Dictionary<string, LogLevel?>()
                {
                    { LoggerRemoteTestName, LogLevel.Warning }
                };
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events are collected for wildcard categories.
        /// </summary>
        [SkippableFact]
        public async Task TestLogsWildcardCategory()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2568");
            }
            using Stream outputStream = await GetLogsAsync(settings =>
            {
                settings.UseAppFilters = false;
                settings.LogLevel = LogLevel.Critical;
                settings.FilterSpecs = new Dictionary<string, LogLevel?>()
                {
                    { WildcardCagetoryName, LogLevel.Warning },
                    { LoggerRemoteTestName, LogLevel.Error },
                };
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using var reader = new StreamReader(outputStream);

            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        private async Task<Stream> GetLogsAsync(Action<EventLogsPipelineSettings> settingsCallback = null)
        {
            var outputStream = new MemoryStream();

            await using (var testExecution = StartTraceeProcess(LoggerRemoteTestName))
            {
                //TestRunner should account for start delay to make sure that the diagnostic pipe is available.

                using var loggerFactory = new LoggerFactory(new[] { new TestStreamingLoggerProvider(outputStream) });
                var client = new DiagnosticsClient(testExecution.TestRunner.Pid);

                var logSettings = new EventLogsPipelineSettings { Duration = Timeout.InfiniteTimeSpan };
                if (null != settingsCallback)
                {
                    settingsCallback(logSettings);
                }
                await using var pipeline = new EventLogsPipeline(client, logSettings, loggerFactory);

                await PipelineTestUtilities.ExecutePipelineWithDebugee(_output, pipeline, testExecution);
            }

            outputStream.Position = 0L;

            return outputStream;
        }

        private static void ValidateLoggerRemoteCategoryInformationMessage(StreamReader reader)
        {
            string message = reader.ReadLine();
            Assert.NotNull(message);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(message);
            Assert.Equal("Some warning message with 6", result.Message);
            Assert.Equal(LoggerRemoteTestName, result.Category);
            Assert.Equal("Information", result.LogLevel);
            Assert.Equal(0, result.EventId);
            Assert.Equal(string.Empty, result.EventName);
            Validate(result.Scopes, ("BoolValue", "true"), ("StringValue", "test"), ("IntValue", "5"));
            Validate(result.Arguments, ("arg", "6"));
        }

        private static void ValidateLoggerRemoteCategoryWarningMessage(StreamReader reader)
        {
            string message = reader.ReadLine();
            Assert.NotNull(message);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(message);
            Assert.Equal("Another message", result.Message);
            Assert.Equal(LoggerRemoteTestName, result.Category);
            Assert.Equal("Warning", result.LogLevel);
            Assert.Equal(7, result.EventId);
            Assert.Equal("AnotherEventId", result.EventName);
            Assert.Equal(0, result.Scopes.Count);
            //We are expecting only the original format
            Assert.Equal(1, result.Arguments.Count);
        }

        private static void ValidateAppLoggerCategoryInformationMessage(StreamReader reader)
        {
            string message = reader.ReadLine();
            Assert.NotNull(message);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(message);
            Assert.Equal("Information message.", result.Message);
            Assert.Equal(AppLoggerCategoryName, result.Category);
            Assert.Equal("Information", result.LogLevel);
            Assert.Equal(0, result.EventId);
            Assert.Equal(string.Empty, result.EventName);
            Assert.Equal(0, result.Scopes.Count);
            //We are expecting only the original format
            Assert.Equal(1, result.Arguments.Count);
        }

        private static void ValidateAppLoggerCategoryWarningMessage(StreamReader reader)
        {
            string message = reader.ReadLine();
            Assert.NotNull(message);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(message);
            Assert.Equal("Warning message.", result.Message);
            Assert.Equal(AppLoggerCategoryName, result.Category);
            Assert.Equal("Warning", result.LogLevel);
            Assert.Equal(5, result.EventId);
            Assert.Equal("WarningEventId", result.EventName);
            Assert.Equal(0, result.Scopes.Count);
            //We are expecting only the original format
            Assert.Equal(1, result.Arguments.Count);
        }

        private static void ValidateAppLoggerCategoryErrorMessage(StreamReader reader)
        {
            string message = reader.ReadLine();
            Assert.NotNull(message);

            LoggerTestResult result = JsonSerializer.Deserialize<LoggerTestResult>(message);
            Assert.Equal("Error message.", result.Message);
            Assert.Equal(AppLoggerCategoryName, result.Category);
            Assert.Equal("Error", result.LogLevel);
            Assert.Equal(0, result.EventId);
            Assert.Equal(string.Empty, result.EventName);
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
            public int EventId { get; set; }
            public string EventName { get; set; }
            public string Message { get; set; }
            public IDictionary<string, JsonElement> Arguments { get; set; }
            public IDictionary<string, JsonElement> Scopes { get; set; }
        }
    }
}
