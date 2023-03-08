// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventLogsPipelineUnitTests
    {
        private const string WildcardCagetoryName = "*";
        private const string AppLoggerCategoryName = "AppLoggerCategory";
        private const string LoggerRemoteTestName = "LoggerRemoteTest";

        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public EventLogsPipelineUnitTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Test that all log events are collected if no filters are specified.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsAllCategoriesAllLevels(TestConfiguration config)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(config, settings => {
                settings.UseAppFilters = false;
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using StreamReader reader = new(outputStream);

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
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsAllCategoriesDefaultLevel(TestConfiguration config)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(config, settings => {
                settings.UseAppFilters = false;
                settings.LogLevel = LogLevel.Warning;
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using StreamReader reader = new(outputStream);

            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events at the default level are collected for categories without a specified level.
        /// </summary>
        [SkippableTheory(Skip = "Unreliable test https://github.com/dotnet/diagnostics/issues/3143"), MemberData(nameof(Configurations))]
        public async Task TestLogsAllCategoriesDefaultLevelFallback(TestConfiguration config)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(config, settings => {
                settings.UseAppFilters = false;
                settings.LogLevel = LogLevel.Error;
                settings.FilterSpecs = new Dictionary<string, LogLevel?>()
                {
                    { AppLoggerCategoryName, null },
                    { LoggerRemoteTestName, LogLevel.Trace }
                };
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using StreamReader reader = new(outputStream);

            ValidateLoggerRemoteCategoryInformationMessage(reader);
            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that LogLevel.None is not supported as the default log level.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsAllCategoriesDefaultLevelNoneNotSupported(TestConfiguration config)
        {
            // Pipeline should throw PipelineException with inner exception of NotSupportedException.
            PipelineException exception = await Assert.ThrowsAsync<PipelineException>(
                () => GetLogsAsync(
                    config,
                    settings => {
                        settings.UseAppFilters = false;
                        settings.LogLevel = LogLevel.None;
                    }));

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }

        /// <summary>
        /// Test that log events are collected for the categories and levels specified by the application.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsUseAppFilters(TestConfiguration config)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(config);

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using StreamReader reader = new(outputStream);

            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events are collected for the categories and levels specified by the application
        /// and for the categories and levels specified in the filter specs.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsUseAppFiltersAndFilterSpecs(TestConfiguration config)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(config, settings => {
                settings.FilterSpecs = new Dictionary<string, LogLevel?>()
                {
                    { LoggerRemoteTestName, LogLevel.Warning }
                };
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using StreamReader reader = new(outputStream);

            ValidateLoggerRemoteCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        /// <summary>
        /// Test that log events are collected for wildcard categories.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task TestLogsWildcardCategory(TestConfiguration config)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SkipTestException("https://github.com/dotnet/diagnostics/issues/2541");
            }

            using Stream outputStream = await GetLogsAsync(config, settings => {
                settings.UseAppFilters = false;
                settings.LogLevel = LogLevel.Critical;
                settings.FilterSpecs = new Dictionary<string, LogLevel?>()
                {
                    { WildcardCagetoryName, LogLevel.Warning },
                    { LoggerRemoteTestName, LogLevel.Error },
                };
            });

            Assert.True(outputStream.Length > 0, "No data written by logging process.");

            using StreamReader reader = new(outputStream);

            ValidateAppLoggerCategoryWarningMessage(reader);
            ValidateAppLoggerCategoryErrorMessage(reader);

            Assert.True(reader.EndOfStream, "Expected to have read all entries from stream.");
        }

        private async Task<Stream> GetLogsAsync(TestConfiguration config, Action<EventLogsPipelineSettings> settingsCallback = null)
        {
            MemoryStream outputStream = new();

            await using (TestRunner testRunner = await PipelineTestUtilities.StartProcess(config, LoggerRemoteTestName, _output))
            {
                using LoggerFactory loggerFactory = new(new[] { new TestStreamingLoggerProvider(outputStream) });
                DiagnosticsClient client = new(testRunner.Pid);

                EventLogsPipelineSettings logSettings = new() { Duration = Timeout.InfiniteTimeSpan };
                if (null != settingsCallback)
                {
                    settingsCallback(logSettings);
                }
                await using EventLogsPipeline pipeline = new(client, logSettings, loggerFactory);

                await PipelineTestUtilities.ExecutePipelineWithTracee(pipeline, testRunner);
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
            Validate(result.Arguments, ("Arg", "6"));
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
            foreach ((string key, object value) expectedValue in expectedValues)
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
            public int EventId { get; set; }
            public string EventName { get; set; }
            public string Message { get; set; }
            public IDictionary<string, JsonElement> Arguments { get; set; }
            public IDictionary<string, JsonElement> Scopes { get; set; }
        }
    }
}
