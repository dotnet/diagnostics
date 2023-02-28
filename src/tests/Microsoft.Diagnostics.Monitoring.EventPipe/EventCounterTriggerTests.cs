// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter;
using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Pipelines;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class EventCounterTriggerTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public EventCounterTriggerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Tests the validation of the fields of the trigger settings.
        /// </summary>
        [Fact]
        public void EventCounterTriggerSettingsValidationTest()
        {
            EventCounterTriggerSettings settings = new();

            // ProviderName is required.
            ValidateRequiredFieldValidation(
                settings,
                nameof(EventCounterTriggerSettings.ProviderName));

            settings.ProviderName = EventCounterConstants.RuntimeProviderName;

            // CounterName is required.
            ValidateRequiredFieldValidation(
                settings,
                nameof(EventCounterTriggerSettings.CounterName));

            settings.CounterName = "exception-count";

            // SlidingWindowDuration must be specified within valid range.
            ValidateRangeFieldValidation<TimeSpan>(
                settings,
                nameof(EventCounterTriggerSettings.SlidingWindowDuration),
                EventCounterTriggerSettings.SlidingWindowDuration_MinValue,
                EventCounterTriggerSettings.SlidingWindowDuration_MaxValue);

            settings.SlidingWindowDuration = TimeSpan.FromSeconds(15);

            // CounterIntervalSeconds must be specified within valid range.
            ValidateRangeFieldValidation<int>(
                settings,
                nameof(EventCounterTriggerSettings.CounterIntervalSeconds),
                EventCounterTriggerSettings.CounterIntervalSeconds_MinValue.ToString(CultureInfo.InvariantCulture),
                EventCounterTriggerSettings.CounterIntervalSeconds_MaxValue.ToString(CultureInfo.InvariantCulture));

            settings.CounterIntervalSeconds = 2;

            // Either GreaterThan or LessThan must be specified
            ValidateFieldValidation(
                settings,
                EventCounterTriggerSettings.EitherGreaterThanLessThanMessage,
                new[] { nameof(EventCounterTriggerSettings.GreaterThan), nameof(EventCounterTriggerSettings.LessThan) });

            settings.GreaterThan = 10;

            // Settings object should now pass validation
            EventCounterTrigger trigger = new(settings);

            // GreaterThan must be less than LessThan
            settings.LessThan = 5;
            ValidateFieldValidation(
                settings,
                EventCounterTriggerSettings.GreaterThanMustBeLessThanLessThanMessage,
                new[] { nameof(EventCounterTriggerSettings.GreaterThan), nameof(EventCounterTriggerSettings.LessThan) });
        }

        /// <summary>
        /// Validates that the usage of the settings will result in a ValidationException thrown
        /// with the expected error message and member names.
        /// </summary>
        private void ValidateFieldValidation(EventCounterTriggerSettings settings, string expectedMessage, string[] expectedMemberNames)
        {
            var exception = Assert.Throws<ValidationException>(() => new EventCounterTrigger(settings));

            Assert.NotNull(exception.ValidationResult);

            Assert.Equal(expectedMessage, exception.ValidationResult.ErrorMessage);

            Assert.Equal(expectedMemberNames, exception.ValidationResult.MemberNames);
        }

        /// <summary>
        /// Validates that the given member name will yield a requiredness validation exception when not specified.
        /// </summary>
        private void ValidateRequiredFieldValidation(EventCounterTriggerSettings settings, string memberName)
        {
            RequiredAttribute requiredAttribute = new();
            ValidateFieldValidation(settings, requiredAttribute.FormatErrorMessage(memberName), new[] { memberName });
        }

        /// <summary>
        /// Validates that the given member name will yield a range validation exception when not in the valid range.
        /// </summary>
        private void ValidateRangeFieldValidation<T>(EventCounterTriggerSettings settings, string memberName, string min, string max)
        {
            RangeAttribute rangeAttribute = new(typeof(T), min, max);
            ValidateFieldValidation(settings, rangeAttribute.FormatErrorMessage(memberName), new[] { memberName });
        }

        /// <summary>
        /// Test that the trigger condition can be satisfied when detecting counter
        /// values higher than the specified threshold for a duration of time.
        /// </summary>
        [Fact]
        public void EventCounterTriggerGreaterThanTest()
        {
            // The counter value must be greater than 0.70 for at least 3 seconds.
            const double Threshold = 70; // 70%
            const int Interval = 1; // 1 second
            TimeSpan WindowDuration = TimeSpan.FromSeconds(3);

            CpuData[] data = new CpuData[]
            {
                new(65, false),
                new(67, false),
                new(71, false),
                new(73, false),
                new(74, null), // Unknown depending if sum of intervals is larger than window
                new(72, true),
                new(71, true),
                new(70, false), // Value must be greater than threshold
                new(68, false),
                new(66, false),
                new(70, false),
                new(71, false),
                new(74, false),
                new(73, null), // Unknown depending if sum of intervals is larger than window
                new(75, true),
                new(72, true),
                new(73, true),
                new(71, true),
                new(69, false),
                new(67, false)
            };

            EventCounterTriggerSettings settings = new()
            {
                ProviderName = EventCounterConstants.RuntimeProviderName,
                CounterName = EventCounterConstants.CpuUsageCounterName,
                GreaterThan = Threshold,
                CounterIntervalSeconds = Interval,
                SlidingWindowDuration = WindowDuration
            };

            SimulateDataVerifyTrigger(settings, data);
        }

        /// <summary>
        /// Test that the trigger condition can be satisfied when detecting counter
        /// values lower than the specified threshold for a duration of time.
        /// </summary>
        [Fact]
        public void EventCounterTriggerLessThanTest()
        {
            // The counter value must be less than 0.70 for at least 3 seconds.
            const double Threshold = 70; // 70%
            const int Interval = 1; // 1 second
            TimeSpan WindowDuration = TimeSpan.FromSeconds(3);

            CpuData[] data = new CpuData[]
            {
                new(65, false),
                new(67, false),
                new(66, null), // Unknown depending if sum of intervals is larger than window
                new(68, true),
                new(69, true),
                new(70, false), // Value must be less than threshold
                new(71, false),
                new(68, false),
                new(66, false),
                new(68, null), // Unknown depending if sum of intervals is larger than window
                new(67, true),
                new(65, true),
                new(64, true),
                new(71, false),
                new(73, false)
            };

            EventCounterTriggerSettings settings = new()
            {
                ProviderName = EventCounterConstants.RuntimeProviderName,
                CounterName = EventCounterConstants.CpuUsageCounterName,
                LessThan = Threshold,
                CounterIntervalSeconds = Interval,
                SlidingWindowDuration = WindowDuration
            };

            SimulateDataVerifyTrigger(settings, data);
        }

        /// <summary>
        /// Test that the trigger condition can be satisfied when detecting counter
        /// values that fall between two thresholds for a duration of time.
        /// </summary>
        [Fact]
        public void EventCounterTriggerRangeTest()
        {
            // The counter value must be between 0.25 and 0.35 for at least 8 seconds.
            const double LowerThreshold = 25; // 25%
            const double UpperThreshold = 35; // 35%
            const int Interval = 2; // 2 seconds
            TimeSpan WindowDuration = TimeSpan.FromSeconds(8);

            CpuData[] data = new CpuData[]
            {
                new(23, false),
                new(25, false),
                new(26, false),
                new(27, false),
                new(28, false),
                new(29, null), // Unknown depending if sum of intervals is larger than window
                new(30, true),
                new(31, true),
                new(33, true),
                new(35, false),
                new(37, false),
                new(34, false),
                new(33, false),
                new(31, false),
                new(30, null), // Unknown depending if sum of intervals is larger than window
                new(29, true),
                new(27, true),
                new(26, true),
                new(24, false)
            };

            EventCounterTriggerSettings settings = new()
            {
                ProviderName = EventCounterConstants.RuntimeProviderName,
                CounterName = EventCounterConstants.CpuUsageCounterName,
                GreaterThan = LowerThreshold,
                LessThan = UpperThreshold,
                CounterIntervalSeconds = Interval,
                SlidingWindowDuration = WindowDuration
            };

            SimulateDataVerifyTrigger(settings, data);
        }

        /// <summary>
        /// Test that the trigger condition will not be satisfied if successive
        /// counter events are missing from the stream (e.g. events are dropped due
        /// to event pipe buffer being filled).
        /// </summary>
        [Fact]
        public void EventCounterTriggerDropTest()
        {
            // The counter value must be greater than 0.50 for at least 10 seconds.
            const double Threshold = 50; // 50%
            const int Interval = 2; // 2 second
            TimeSpan WindowDuration = TimeSpan.FromSeconds(10);

            CpuData[] data = new CpuData[]
            {
                new(53, false),
                new(54, false),
                new(51, false),
                new(52, false),
                new(54, null), // Unknown depending if sum of intervals is larger than window
                new(53, true),
                new(52, true, drop: true),
                new(51, false),
                new(54, false),
                new(58, false),
                new(53, false),
                new(54, null), // Unknown depending if sum of intervals is larger than window
                new(51, true),
                new(54, true),
                new(54, true, drop: true),
                new(52, false),
                new(57, false),
                new(59, false),
                new(54, false),
                new(53, null), // Unknown depending if sum of intervals is larger than window
                new(51, true),
                new(53, true),
                new(47, false)
            };

            EventCounterTriggerSettings settings = new()
            {
                ProviderName = EventCounterConstants.RuntimeProviderName,
                CounterName = EventCounterConstants.CpuUsageCounterName,
                GreaterThan = Threshold,
                CounterIntervalSeconds = Interval,
                SlidingWindowDuration = WindowDuration
            };

            SimulateDataVerifyTrigger(settings, data);
        }

        /// <summary>
        /// Tests that the trigger condition can be detected on a live application
        /// using the EventPipeTriggerPipeline.
        /// </summary>
        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task EventCounterTriggerWithEventPipePipelineTest(TestConfiguration config)
        {
            if (config.RuntimeFrameworkVersionMajor < 6)
            {
                throw new SkipTestException("Unreliable on .NET 3.1");
            }
            EventCounterTriggerSettings settings = new()
            {
                ProviderName = EventCounterConstants.RuntimeProviderName,
                CounterName = EventCounterConstants.CpuUsageCounterName,
                GreaterThan = 5,
                SlidingWindowDuration = TimeSpan.FromSeconds(3),
                CounterIntervalSeconds = 1
            };

            await using (var testRunner = await PipelineTestUtilities.StartProcess(config, "TriggerRemoteTest SpinWait10", _output, testProcessTimeout: 2 * 60 * 1000))
            {
                DiagnosticsClient client = new(testRunner.Pid);

                TaskCompletionSource<object> waitSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

                await using EventPipeTriggerPipeline<EventCounterTriggerSettings> pipeline = new(
                    client,
                    new EventPipeTriggerPipelineSettings<EventCounterTriggerSettings>
                    {
                        Configuration = EventCounterTrigger.CreateConfiguration(settings),
                        TriggerFactory = new EventCounterTriggerFactory(),
                        TriggerSettings = settings,
                        Duration = Timeout.InfiniteTimeSpan
                    },
                    traceEvent => {
                        waitSource.TrySetResult(null);
                    });

                await PipelineTestUtilities.ExecutePipelineWithTracee(
                    pipeline,
                    testRunner,
                    waitSource);

                Assert.True(waitSource.Task.IsCompletedSuccessfully);
            }
        }

        /// <summary>
        /// Run the specified sample CPU data through a simple simulation to test the capabilities
        /// of the event counter trigger. This uses a random number seed to generate random variations
        /// in timestamp and interval data.
        /// </summary>
        private void SimulateDataVerifyTrigger(EventCounterTriggerSettings settings, CpuData[] cpuData)
        {
            Random random = new Random();
            int seed = random.Next();
            _output.WriteLine("Simulation seed: {0}", seed);
            SimulateDataVerifyTrigger(settings, cpuData, seed);
        }

        /// <summary>
        /// Run the specified sample CPU data through a simple simulation to test the capabilities
        /// of the event counter trigger. This uses the specified seed value to seed the RNG that produces
        /// random variations in timestamp and interval data; allows for replayability of generated variations.
        /// </summary>
        private void SimulateDataVerifyTrigger(EventCounterTriggerSettings settings, CpuData[] cpuData, int seed)
        {
            EventCounterTriggerImpl trigger = new(settings);

            CpuUsagePayloadFactory payloadFactory = new(seed, settings.CounterIntervalSeconds);

            for (int i = 0; i < cpuData.Length; i++)
            {
                ref CpuData data = ref cpuData[i];
                _output.WriteLine("Data: Value={0}, Expected={1}, Drop={2}", data.Value, data.Result, data.Drop);
                ICounterPayload payload = payloadFactory.CreateNext(data.Value);
                if (data.Drop)
                {
                    continue;
                }
                bool actualResult = trigger.HasSatisfiedCondition(payload);
                if (data.Result.HasValue)
                {
                    Assert.Equal(data.Result.Value, actualResult);
                }
            }
        }

        private sealed class CpuData
        {
            public CpuData(double value, bool? result, bool drop = false)
            {
                Drop = drop;
                Result = result;
                Value = value;
            }

            /// <summary>
            /// Specifies if the data should be "dropped" to simulate dropping of events.
            /// </summary>
            public bool Drop { get; }

            /// <summary>
            /// The expected result of evaluating the trigger on this data.
            /// </summary>
            public bool? Result { get; }

            /// <summary>
            /// The sample CPU value to be given to the trigger for evaluation.
            /// </summary>
            public double Value { get; }
        }

        /// <summary>
        /// Creates CPU Usage payloads in successive order, simulating the data produced
        /// for the cpu-usage counter from the runtime.
        /// </summary>
        private sealed class CpuUsagePayloadFactory
        {
            private readonly float _intervalSeconds;
            private readonly Random _random;

            private DateTime? _lastTimestamp;

            public CpuUsagePayloadFactory(int seed, float intervalSeconds)
            {
                _intervalSeconds = intervalSeconds;
                _random = new Random(seed);
            }

            /// <summary>
            /// Creates the next counter payload based on the provided value.
            /// </summary>
            /// <remarks>
            /// The timestamp is roughly incremented by the specified interval from the constructor
            /// in order to simulate variations in the timestamp of counter events as seen in real
            /// event data. The actual interval is also roughly generated from specified interval
            /// from the constructor to simulate variations in the collection interval as seen in
            /// real event data.
            /// </remarks>
            public ICounterPayload CreateNext(double value)
            {
                // Add some variance between -5 to 5 milliseconds to simulate "real" interval value.
                float actualInterval = Convert.ToSingle(_intervalSeconds + (_random.NextDouble() / 100) - 0.005);

                if (!_lastTimestamp.HasValue)
                {
                    // Start with the current time
                    _lastTimestamp = DateTime.UtcNow;
                }
                else
                {
                    // Increment timestamp by one whole interval
                    _lastTimestamp = _lastTimestamp.Value.AddSeconds(actualInterval);
                }

                // Add some variance between -5 and 5 milliseconds to simulate "real" timestamp
                _lastTimestamp = _lastTimestamp.Value.AddMilliseconds((10 * _random.NextDouble()) - 5);

                return new CounterPayload(
                    _lastTimestamp.Value,
                    EventCounterConstants.RuntimeProviderName,
                    EventCounterConstants.CpuUsageCounterName,
                    EventCounterConstants.CpuUsageDisplayName,
                    EventCounterConstants.CpuUsageUnits,
                    value,
                    CounterType.Metric,
                    actualInterval,
                    null);
            }
        }

        /// <summary>
        /// Validates that metadata from TraceEvent payloads is parsed correctly.
        /// </summary>
        [Fact]
        public void ValidateMetadataParsing_Success()
        {
            const string key1 = "K1";
            const string value1 = "V1";
            const string key2 = "K2";
            const string value2 = "V:2";
            IDictionary<string, string> metadataDict = CounterUtilities.GetMetadata($"{key1}:{value1},{key2}:{value2}");

            Assert.Equal(2, metadataDict.Count);
            Assert.Equal(value1, metadataDict[key1]);
            Assert.Equal(value2, metadataDict[key2]);
        }

        /// <summary>
        /// Validates that metadata with an invalid format from TraceEvent payloads is handled correctly.
        /// </summary>
        [Theory]
        [InlineData("K1:V,1")]
        [InlineData("K,1:V")]
        [InlineData("K1")]
        public void ValidateMetadataParsing_Failure(string invalidMetadata)
        {
            IDictionary<string, string> metadataDict = CounterUtilities.GetMetadata(invalidMetadata);

            Assert.Empty(metadataDict);
        }
    }
}
