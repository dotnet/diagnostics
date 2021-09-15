// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestStatusTriggerSettings : AspNetTriggerSettings
    {
        private const string StatusRegex = "[1-5][0-9]{2}";
        private static readonly string RangeRegex = FormattableString.Invariant($"^{StatusRegex}(-{StatusRegex})?$");

        /// <summary>
        /// Specifies the set of status codes for the trigger. This can be individual codes or ranges.
        /// E.g. 200;400-500
        /// </summary>
        [Required]
        public string[] StatusCodes { get; set; }

        public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            List<ValidationResult> results = new List<ValidationResult>();

            foreach(string statusCode in StatusCodes)
            {
                if (!Regex.IsMatch(statusCode, RangeRegex))
                {
                    results.Add(new ValidationResult($"'{statusCode}' is not in the correct format.",
                        new[] { nameof(StatusCodes) }));
                }
            }

            return results.Concat(base.Validate(validationContext));
        }
    }
}
