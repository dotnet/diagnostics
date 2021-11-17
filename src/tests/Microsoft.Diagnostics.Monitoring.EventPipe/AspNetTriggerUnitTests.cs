// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet;
using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Pipelines;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
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

            var s1 = generator.CreateEvent(DateTime.UtcNow);

            // These should not trigger anything because they are not included
            var s2 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(30), "/notIncluded");
            var s3 = generator.CreateEvent(s2.Timestamp, "/notIncluded");
            var s4 = generator.CreateEvent(s2.Timestamp, "/notIncluded");

            var s5 = generator.CreateEvent(s2.Timestamp);

            //Pushes the first event out of sliding window
            var s6 = generator.CreateEvent(s2.Timestamp + TimeSpan.FromSeconds(40));

            var s7 = generator.CreateEvent(s6.Timestamp + TimeSpan.FromSeconds(0.5));

            ValidateTriggers(trigger, s1, s2, s3, s4, s5, s6, s7);
        }

        [Fact]
        public void TestAspNetRequestCountExclusions()
        {
            AspNetRequestCountTriggerSettings settings = new()
            {
                ExcludePaths = new[] {"/"},
                RequestCount = 3,
                SlidingWindowDuration = TimeSpan.FromMinutes(1)
            };

            AspNetRequestCountTriggerFactory factory = new();
            var trigger = (AspNetRequestCountTrigger)factory.Create(settings);

            PayloadGenerator generator = new();

            // These should not trigger anything because they are excluded
            var s1 = generator.CreateEvent(DateTime.UtcNow);
            var s2 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            var s3 = generator.CreateEvent(s2.Timestamp);
            var s4 = generator.CreateEvent(s2.Timestamp);

            var s5 = generator.CreateEvent(s2.Timestamp, "/notExcluded");
            var s6 = generator.CreateEvent(s2.Timestamp, "/notExcluded");
            var s7 = generator.CreateEvent(s6.Timestamp + TimeSpan.FromSeconds(10), "/notExcluded");

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

            var s1 = generator.CreateEvent(DateTime.UtcNow);
            var e1 = generator.CreateEvent(s1, TimeSpan.FromSeconds(3.1).Ticks);

            var s2 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            var e2 = generator.CreateEvent(s2, TimeSpan.FromSeconds(10).Ticks);

            //does not exceed duration
            var s3 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            var e3 = generator.CreateEvent(s3, TimeSpan.FromSeconds(1).Ticks);

            //pushes the sliding window past all prevoius events
            var s4 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromMinutes(5));
            var e4 = generator.CreateEvent(s4, TimeSpan.FromSeconds(15).Ticks);

            var s5 = generator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            var e5 = generator.CreateEvent(s5, TimeSpan.FromSeconds(10).Ticks);

            var s6 = generator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            var e6 = generator.CreateEvent(s6, TimeSpan.FromSeconds(20).Ticks);

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

            var s1 = generator.CreateEvent(DateTime.UtcNow);
            var e1 = generator.CreateEvent(s1, TimeSpan.FromMinutes(1).Ticks);

            var s2 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            var e2 = generator.CreateEvent(s2, TimeSpan.FromMinutes(2).Ticks);

            var s3 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(2));
            var e3 = generator.CreateEvent(s3, TimeSpan.FromMinutes(3).Ticks);

            var h1 = generator.CreateCounterEvent(s1.Timestamp + TimeSpan.FromSeconds(15));

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

            var s1 = generator.CreateEvent(DateTime.UtcNow);
            var e1 = generator.CreateEvent(s1, TimeSpan.FromSeconds(3.1).Ticks, statusCode: 404);

            var s2 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            var e2 = generator.CreateEvent(s2, TimeSpan.FromSeconds(10).Ticks, statusCode: 420);

            //does not meet status code
            var s3 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromSeconds(1));
            var e3 = generator.CreateEvent(s3, TimeSpan.FromSeconds(1).Ticks);

            //pushes the sliding window past all prevoius events
            var s4 = generator.CreateEvent(s1.Timestamp + TimeSpan.FromMinutes(5));
            var e4 = generator.CreateEvent(s4, TimeSpan.FromSeconds(15).Ticks, statusCode: 520);

            var s5 = generator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            var e5 = generator.CreateEvent(s5, TimeSpan.FromSeconds(10).Ticks, statusCode: 521);

            //does not meet status code
            var s6 = generator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            var e6 = generator.CreateEvent(s5, TimeSpan.FromSeconds(10).Ticks);

            var s7 = generator.CreateEvent(s4.Timestamp + TimeSpan.FromSeconds(1));
            var e7 = generator.CreateEvent(s7, TimeSpan.FromSeconds(20).Ticks, statusCode: 404);

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

        private static void ValidateTriggers<T>(AspNetTrigger<T> requestTrigger, params SimulatedTraceEvent[] events) where T: AspNetTriggerSettings
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
            public SimulatedTraceEvent CreateCounterEvent(DateTime timestamp)
            {
                return new SimulatedTraceEvent { Timestamp = timestamp, EventType = AspnetTriggerEventType.Heartbeat };
            }

            public SimulatedTraceEvent CreateEvent(DateTime timestamp, string path = "/", string activityId = null)
            {
                return new SimulatedTraceEvent
                {
                    Timestamp = timestamp,
                    EventType = AspnetTriggerEventType.Start,
                    ActivityId =  activityId ?? Guid.NewGuid().ToString(),
                    Path = path
                };
            }

            public SimulatedTraceEvent CreateEvent(SimulatedTraceEvent previousEvent, long duration, int statusCode = 200, string path = null, string activityId = null)
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
