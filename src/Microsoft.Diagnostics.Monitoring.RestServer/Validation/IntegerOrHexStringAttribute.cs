// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Validation
{
    public class IntegerOrHexStringAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (!(value is string stringValue))
            {
                return new ValidationResult("Value must be of type string.");
            }
            else if (!TryParse(stringValue, out _, out string error))
            {
                return new ValidationResult(error);
            }
            return ValidationResult.Success;
        }

        public static bool TryParse(string value, out long result, out string error)
        {
            result = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Value cannot be null, empty, or whitespace.";
                return false;
            }
            else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                // AllowHexSpecifier requires that the "0x" is removed before attempting to parse.
                // It parses the actual value, not the "0x" syntax prefix.
                if (!long.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result))
                {
                    error = FormattableString.Invariant($"The value '{value}' is not a valid hexadecimal number.");
                    return false;
                }
            }
            else
            {
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                {
                    error = FormattableString.Invariant($"The value '{value}' is not a valid integer.");
                    return false;
                }
            }

            return true;
        }
    }
}
