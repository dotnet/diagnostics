// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter
{
    /// <summary>
    /// The settings for the <see cref="EventCounterTrigger"/>.
    /// </summary>
    internal sealed class EventCounterTriggerSettings :
        IValidatableObject
    {
        internal const float CounterIntervalSeconds_MaxValue = 24 * 60 * 60; // 1 day
        internal const float CounterIntervalSeconds_MinValue = 1; // 1 second

        internal const string EitherGreaterThanLessThanMessage = "Either the " + nameof(GreaterThan) + " field or the " + nameof(LessThan) + " field are required.";

        internal const string GreaterThanMustBeLessThanLessThanMessage = "The " + nameof(GreaterThan) + " field must be less than the " + nameof(LessThan) + " field.";

        internal const string SlidingWindowDuration_MaxValue = "1.00:00:00"; // 1 day
        internal const string SlidingWindowDuration_MinValue = "00:00:01"; // 1 second

        /// <summary>
        /// The name of the event provider from which counters will be monitored.
        /// </summary>
        [Required]
        public string ProviderName { get; set; }

        /// <summary>
        /// The name of the event counter from the event provider to monitor.
        /// </summary>
        [Required]
        public string CounterName { get; set; }

        /// <summary>
        /// The lower bound threshold that the event counter value must hold for
        /// the duration specified in <see cref="SlidingWindowDuration"/>.
        /// </summary>
        public double? GreaterThan { get; set; }

        /// <summary>
        /// The upper bound threshold that the event counter value must hold for
        /// the duration specified in <see cref="SlidingWindowDuration"/>.
        /// </summary>
        public double? LessThan { get; set; }

        /// <summary>
        /// The sliding duration of time in which the event counter must maintain a value
        /// above, below, or between the thresholds specified by <see cref="GreaterThan"/> and <see cref="LessThan"/>.
        /// </summary>
        [Range(typeof(TimeSpan), SlidingWindowDuration_MinValue, SlidingWindowDuration_MaxValue)]
        public TimeSpan SlidingWindowDuration { get; set; }

        /// <summary>
        /// The sampling interval of the event counter.
        /// </summary>
        [Range(CounterIntervalSeconds_MinValue, CounterIntervalSeconds_MaxValue)]
        public float CounterIntervalSeconds { get; set; }

        IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
        {
            List<ValidationResult> results = new();

            if (!GreaterThan.HasValue && !LessThan.HasValue)
            {
                results.Add(new ValidationResult(
                    EitherGreaterThanLessThanMessage,
                    new[]
                    {
                        nameof(GreaterThan),
                        nameof(LessThan)
                    }));
            }
            else if (GreaterThan.HasValue && LessThan.HasValue)
            {
                if (GreaterThan.Value >= LessThan.Value)
                {
                    results.Add(new ValidationResult(
                        GreaterThanMustBeLessThanLessThanMessage,
                        new[]
                        {
                            nameof(GreaterThan),
                            nameof(LessThan)
                        }));
                }
            }

            return results;
        }
    }
}
