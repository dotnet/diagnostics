// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    /// <summary>
    /// Base class for all Asp.net trigger settings.
    /// </summary>
    internal class AspNetTriggerSettings
    {
        public const string SlidingWindowDuration_MaxValue = "1.00:00:00"; // 1 day
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
        [Range(1, long.MaxValue)]
        public long RequestCount { get; set; }

        /// <summary>
        /// List of request paths to include in the trigger condition, such as "/" and "/About".
        /// </summary>
        [CustomValidation(typeof(IncludesPathValidator), nameof(IncludesPathValidator.ValidatePath))]
        public string[] IncludePaths { get; set; }

        /// <summary>
        /// List of request paths to exclude in the trigger condition.
        /// </summary>
        [CustomValidation(typeof(ExcludesPathValidator), nameof(ExcludesPathValidator.ValidatePath))]
        public string[] ExcludePaths { get; set; }
    }

    internal static class PathValidator
    {
        public static ValidationResult ValidatePath(string[] paths, string[] members)
        {
            //While not an error, using *** or more causes confusing and unexpected matching.
            if (paths?.Any(p => p.IndexOf("***", StringComparison.Ordinal) >= 0) == true)
            {
                return new ValidationResult("Only * or **/ wildcard chararcters are allowed.", members);
            }
            return ValidationResult.Success;
        }
    }

    public static class IncludesPathValidator
    {
        private static readonly string[] _validationMembers = new[] { nameof(AspNetTriggerSettings.IncludePaths) };

        public static ValidationResult ValidatePath(string[] paths) => PathValidator.ValidatePath(paths, _validationMembers);
    }

    public static class ExcludesPathValidator
    {
        private static readonly string[] _validationMembers = new[] { nameof(AspNetTriggerSettings.ExcludePaths) };

        public static ValidationResult ValidatePath(string[] paths) => PathValidator.ValidatePath(paths, _validationMembers);
    }
}
