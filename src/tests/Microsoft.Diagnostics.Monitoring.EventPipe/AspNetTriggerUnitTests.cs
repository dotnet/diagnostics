// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class AspNetTriggerUnitTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public AspNetTriggerUnitTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public void TestAspNetRequestCount()
        {
            AspNetRequestCountTriggerSettings settings = new()
            {
                IncludePaths = new[] { "/" },
                RequestCount = 3,
                SlidingWindowDuration = TimeSpan.FromMinutes(1)
            };

            AspNetRequestCountTriggerFactory factory = new();
            var trigger = (AspNetRequestCountTrigger)factory.Create(settings);

            PayloadGenerator generator = new();

            SimulatedTraceEvent s1 = PayloadGenerator.CreateEvent(DateTime.UtcNow);

            // These should not trigger anything because they are not included
            SimulatedTraceEvent s2 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(30), "/notIncluded");
            SimulatedTraceEvent s3 = PayloadGenerator.CreateEvent(s2.Timestamp, "/notIncluded");
            SimulatedTraceEvent s4 = PayloadGenerator.CreateEvent(s2.Timestamp, "/notIncluded");

            SimulatedTraceEvent s5 = PayloadGenerator.CreateEvent(s2.Timestamp);

            //Pushes the first event out of sliding window
            SimulatedTraceEvent s6 = PayloadGenerator.CreateEvent(s2.Timestamp + TimeSpan.FromSeconds(40));

            SimulatedTraceEvent s7 = PayloadGenerator.CreateEvent(s6.Timestamp + TimeSpan.FromSeconds(0.5));

            ValidateTriggers(trigger, s1, s2, s3, s4, s5, s6, s7);
        }

        [Fact]
        public void TestAspNetRequestCountExclusions()
        {
            AspNetRequestCountTriggerSettings settings = new()
            {
                ExcludePaths = new[] { "/" },
                RequestCount = 3,
                SlidingWindowDuration = TimeSpan.FromMinutes(1)
            };

            AspNetRequestCountTriggerFactory factory = new();
            var trigger = (AspNetRequestCountTrigger)factory.Create(settings);

            PayloadGenerator generator = new();

            // These should not trigger anything because they are excluded
            SimulatedTraceEvent s1 = PayloadGenerator.CreateEvent(DateTime.UtcNow);
            SimulatedTraceEvent s2 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent s3 = PayloadGenerator.CreateEvent(s2.Timestamp);
            SimulatedTraceEvent s4 = PayloadGenerator.CreateEvent(s2.Timestamp);

            SimulatedTraceEvent s5 = PayloadGenerator.CreateEvent(s2.Timestamp, "/notExcluded");
            SimulatedTraceEvent s6 = PayloadGenerator.CreateEvent(s2.Timestamp, "/notExcluded");
            SimulatedTraceEvent s7 = PayloadGenerator.CreateEvent(s6.Timestamp + TimeSpan.FromSeconds(10), "/notExcluded");

            ValidateTriggers(trigger, s1, s2, s3, s4, s5, s6, s7);
        }

        [Fact]
        public void TestAspNetDuration()
        {
            AspNetRequestDurationTriggerSettings settings = new()
            {
                RequestCount = 3,
                RequestDuration = TimeSpan.FromSeconds(3),
                SlidingWindowDuration = TimeSpan.FromMinutes(1),
            };

            AspNetRequestDurationTriggerFactory factory = new();
            var trigger = (AspNetRequestDurationTrigger)factory.Create(settings);

            PayloadGenerator generator = new();

            SimulatedTraceEvent s1 = PayloadGenerator.CreateEvent(DateTime.UtcNow);
            SimulatedTraceEvent e1 = PayloadGenerator.CreateEvent(s1, TimeSpan.FromSeconds(3.1).Ticks);

            SimulatedTraceEvent s2 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e2 = PayloadGenerator.CreateEvent(s2, TimeSpan.FromSeconds(10).Ticks);

            //does not exceed duration
            SimulatedTraceEvent s3 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e3 = PayloadGenerator.CreateEvent(s3, TimeSpan.FromSeconds(1).Ticks);

            //pushes the sliding window past all prevoius events
            SimulatedTraceEvent s4 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromMinutes(5));
            SimulatedTraceEvent e4 = PayloadGenerator.CreateEvent(s4, TimeSpan.FromSeconds(15).Ticks);

            SimulatedTraceEvent s5 = PayloadGenerator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e5 = PayloadGenerator.CreateEvent(s5, TimeSpan.FromSeconds(10).Ticks);

            SimulatedTraceEvent s6 = PayloadGenerator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e6 = PayloadGenerator.CreateEvent(s6, TimeSpan.FromSeconds(20).Ticks);

            //Reflects actual ordering
            ValidateTriggers(trigger, s1, s2, s3, e3, e1, e2, s4, s5, s6, e5, e4, e6);
        }

        [Fact]
        public void TestAspNetDurationHungRequests()
        {
            AspNetRequestDurationTriggerSettings settings = new()
            {
                RequestCount = 3,
                RequestDuration = TimeSpan.FromSeconds(3),
                SlidingWindowDuration = TimeSpan.FromMinutes(1),
            };

            AspNetRequestDurationTriggerFactory factory = new();
            var trigger = (AspNetRequestDurationTrigger)factory.Create(settings);

            PayloadGenerator generator = new();

            SimulatedTraceEvent s1 = PayloadGenerator.CreateEvent(DateTime.UtcNow);
            SimulatedTraceEvent e1 = PayloadGenerator.CreateEvent(s1, TimeSpan.FromMinutes(1).Ticks);

            SimulatedTraceEvent s2 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e2 = PayloadGenerator.CreateEvent(s2, TimeSpan.FromMinutes(2).Ticks);

            SimulatedTraceEvent s3 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(2));
            SimulatedTraceEvent e3 = PayloadGenerator.CreateEvent(s3, TimeSpan.FromMinutes(3).Ticks);

            SimulatedTraceEvent h1 = PayloadGenerator.CreateCounterEvent(s1.Timestamp + TimeSpan.FromSeconds(15));

            ValidateTriggers(trigger, triggerIndex: 3, s1, s2, s3, h1, e1, e2, e3);
        }

        [Fact]
        public void TestAspNetStatus()
        {
            AspNetRequestStatusTriggerSettings settings = new()
            {
                RequestCount = 3,
                StatusCodes = new StatusCodeRange[] { new(520), new(521), new(400, 500) },
                SlidingWindowDuration = TimeSpan.FromMinutes(1),
            };

            AspNetRequestStatusTriggerFactory factory = new();
            var trigger = (AspNetRequestStatusTrigger)factory.Create(settings);

            PayloadGenerator generator = new();

            SimulatedTraceEvent s1 = PayloadGenerator.CreateEvent(DateTime.UtcNow);
            SimulatedTraceEvent e1 = PayloadGenerator.CreateEvent(s1, TimeSpan.FromSeconds(3.1).Ticks, statusCode: 404);

            SimulatedTraceEvent s2 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e2 = PayloadGenerator.CreateEvent(s2, TimeSpan.FromSeconds(10).Ticks, statusCode: 420);

            //does not meet status code
            SimulatedTraceEvent s3 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e3 = PayloadGenerator.CreateEvent(s3, TimeSpan.FromSeconds(1).Ticks);

            //pushes the sliding window past all prevoius events
            SimulatedTraceEvent s4 = PayloadGenerator.CreateEvent(s1.Timestamp + TimeSpan.FromMinutes(5));
            SimulatedTraceEvent e4 = PayloadGenerator.CreateEvent(s4, TimeSpan.FromSeconds(15).Ticks, statusCode: 520);

            SimulatedTraceEvent s5 = PayloadGenerator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e5 = PayloadGenerator.CreateEvent(s5, TimeSpan.FromSeconds(10).Ticks, statusCode: 521);

            //does not meet status code
            SimulatedTraceEvent s6 = PayloadGenerator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e6 = PayloadGenerator.CreateEvent(s5, TimeSpan.FromSeconds(10).Ticks);

            SimulatedTraceEvent s7 = PayloadGenerator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            SimulatedTraceEvent e7 = PayloadGenerator.CreateEvent(s7, TimeSpan.FromSeconds(20).Ticks, statusCode: 404);

            //Reflects actual ordering
            ValidateTriggers(trigger, s1, s2, s3, e3, e1, e2, s4, s5, s6, s7, e5, e4, e6, e7);
        }

        [Fact]
        public void TestInvalidPaths()
        {
            AspNetRequestCountTriggerSettings settings = new()
            {
                IncludePaths = new[] { "/SomePath/***/*" },
                RequestCount = 3,
                SlidingWindowDuration = TimeSpan.FromMinutes(1)
            };

            AspNetRequestCountTriggerFactory factory = new();
            Assert.Throws<ValidationException>(() => factory.Create(settings));
        }

        private static void ValidateTriggers<T>(AspNetTrigger<T> requestTrigger, params SimulatedTraceEvent[] events) where T : AspNetTriggerSettings
        {
            ValidateTriggers(requestTrigger, events.Length - 1, events);
        }

        private static void ValidateTriggers<T>(AspNetTrigger<T> requestTrigger, int triggerIndex, params SimulatedTraceEvent[] events) where T : AspNetTriggerSettings
        {
            for (int i = 0; i < events.Length; i++)
            {
                bool shouldSatisfy = i == triggerIndex;
                bool validTriggerResult = (shouldSatisfy == requestTrigger.HasSatisfiedCondition(
                        events[i].Timestamp,
                        events[i].EventType,
                        events[i].ActivityId,
                        events[i].Path,
                        events[i].StatusCode,
                        events[i].Duration));

                Assert.True(validTriggerResult, $"Failed at index {i}");
            }
        }

        private sealed class PayloadGenerator
        {
            public static SimulatedTraceEvent CreateCounterEvent(DateTime timestamp)
            {
                return new SimulatedTraceEvent { Timestamp = timestamp, EventType = AspnetTriggerEventType.Heartbeat };
            }

            public static SimulatedTraceEvent CreateEvent(DateTime timestamp, string path = "/", string activityId = null)
            {
                return new SimulatedTraceEvent
                {
                    Timestamp = timestamp,
                    EventType = AspnetTriggerEventType.Start,
                    ActivityId = activityId ?? Guid.NewGuid().ToString(),
                    Path = path
                };
            }

            public static SimulatedTraceEvent CreateEvent(SimulatedTraceEvent previousEvent, long duration, int statusCode = 200, string path = null, string activityId = null)
            {
                Assert.NotNull(previousEvent);
                return new SimulatedTraceEvent
                {
                    ActivityId = activityId ?? previousEvent.ActivityId,
                    Timestamp = previousEvent.Timestamp + TimeSpan.FromTicks(duration),
                    Path = path ?? previousEvent.Path,
                    Duration = duration,
                    EventType = AspnetTriggerEventType.Stop,
                    StatusCode = statusCode
                };
            }
        }

        private sealed class SimulatedTraceEvent
        {
            public DateTime Timestamp { get; set; }

            public AspnetTriggerEventType EventType { get; set; }

            public string ActivityId { get; set; }

            public string Path { get; set; }

            public int? StatusCode { get; set; }

            public long? Duration { get; set; }
        }

    }
}
