// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Shared
{
    internal static class SharedTriggerSettingsValidation
    {
        public static IEnumerable<ValidationResult> Validate(double? GreaterThan, double? LessThan)
        {
            List<ValidationResult> results = new();

            if (!GreaterThan.HasValue && !LessThan.HasValue)
            {
                results.Add(new ValidationResult(
                    SharedTriggerSettingsConstants.EitherGreaterThanLessThanMessage,
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
                        SharedTriggerSettingsConstants.GreaterThanMustBeLessThanLessThanMessage,
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
