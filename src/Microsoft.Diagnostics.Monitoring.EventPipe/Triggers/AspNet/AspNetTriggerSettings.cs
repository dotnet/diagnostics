// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal class AspNetTriggerSettings : IValidatableObject
    {
        public const string SlidingWindowDuration_MaxValue = "01:00:00"; // 1 hour
        public const string SlidingWindowDuration_MinValue = "00:00:01"; // 1 second

        /// <summary>
        /// The sliding duration in which an Asp.net request trigger condition must occur.
        /// </summary>
        [Range(typeof(TimeSpan), SlidingWindowDuration_MinValue, SlidingWindowDuration_MaxValue)]
        public TimeSpan SlidingWindowDuration { get; set; }

        /// <summary>
        /// The amount of requests that must accumulate in the sliding window and meet the trigger condition.
        /// Note that requests that do not meet the condition do NOT reset the count.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int RequestCount { get; set; }

        /// <summary>
        /// List of request paths to include in the trigger condition, such as "/" and "/About".
        /// </summary>
        public string[] IncludePaths { get; set; }

        /// <summary>
        /// List of request paths to exclude in the trigger condition.
        /// </summary>
        public string[] ExcludePaths { get; set; }

        public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            List<ValidationResult> results = new();

            if (IncludePaths?.Any() == true && ExcludePaths?.Any() == true)
            {
                results.Add(new ValidationResult($"Cannot set both {nameof(IncludePaths)} and {nameof(ExcludePaths)}."));
            }

            return results;
        }
    }
}
