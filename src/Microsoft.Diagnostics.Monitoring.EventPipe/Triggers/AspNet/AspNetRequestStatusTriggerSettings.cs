// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestStatusTriggerSettings : AspNetTriggerSettings
    {
        /// <summary>
        /// Specifies the set of status codes for the trigger.
        /// E.g. 200-200;400-500
        /// </summary>
        [Required]
        [MinLength(1)]
        [CustomValidation(typeof(StatusCodeRangeValidator), nameof(StatusCodeRangeValidator.ValidateStatusCodes))]
        public StatusCodeRange[] StatusCodes { get; set; }
    }

    internal struct StatusCodeRange
    {
        public StatusCodeRange(int min) : this(min, min) { }

        public StatusCodeRange(int min, int max)
        {
            Min = min;
            Max = max;
        }

        public int Min { get; set; }
        public int Max { get; set; }
    }

    public static class StatusCodeRangeValidator
    {
        private static readonly string[] _validationMembers = new[] { nameof(AspNetRequestStatusTriggerSettings.StatusCodes) };

        public static ValidationResult ValidateStatusCodes(object statusCodes)
        {
            StatusCodeRange[] statusCodeRanges = (StatusCodeRange[])statusCodes;

            Func<int, bool> validateStatusCode = (int statusCode) => statusCode >= 100 && statusCode < 600;

            foreach (StatusCodeRange statusCodeRange in statusCodeRanges)
            {
                if (statusCodeRange.Min > statusCodeRange.Max)
                {
                    return new ValidationResult($"{nameof(StatusCodeRange.Min)} cannot be greater than {nameof(StatusCodeRange.Max)}",
                        _validationMembers);
                }

                if (!validateStatusCode(statusCodeRange.Min) || !validateStatusCode(statusCodeRange.Max))
                {
                    return new ValidationResult($"Invalid status code", _validationMembers);
                }
            }

            return ValidationResult.Success;
        }
    }

}
