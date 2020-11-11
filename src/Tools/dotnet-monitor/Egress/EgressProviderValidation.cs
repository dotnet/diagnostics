// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    /// <summary>
    /// Helper class for validating egress options.
    /// </summary>
    internal class EgressProviderValidation
    {
        private readonly ILogger _logger;
        private readonly string _providerName;

        public EgressProviderValidation(string providerName, ILogger logger = null)
        {
            _logger = logger;
            _providerName = providerName;
        }

        /// <summary>
        /// Validates that the egress options pass the self-described validation.
        /// </summary>
        /// <param name="value">The instance of the options object.</param>
        /// <returns>True if the options object is valid; otherwise, false.</returns>
        /// <remarks>
        /// Validation errors are logged as warnings.
        /// </remarks>
        public bool TryValidate(object value)
        {
            ValidationContext validationContext = new ValidationContext(value);
            ICollection<ValidationResult> results = new Collection<ValidationResult>();
            if (!Validator.TryValidateObject(value, validationContext, results, validateAllProperties: true))
            {
                if (null != _logger)
                {
                    foreach (ValidationResult result in results)
                    {
                        if (ValidationResult.Success != result)
                        {
                            _logger.LogWarning("Provider '{0}': {1}", _providerName, result.ErrorMessage);
                        }
                    }
                }
                return false;
            }
            return true;
        }
    }
}
