// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressProviderOptions :
        EgressProviderOptions,
        IValidatableObject
    {
        [Required]
        public Uri AccountUri { get; set; }

        public string AccountKey { get; set; }

        public string SharedAccessSignature { get; set; }

        [Required]
        public string ContainerName { get; set; }

        public string BlobPrefix { get; set; }

        IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
        {
            IList<ValidationResult> results = new List<ValidationResult>();

            // One of the authentication keys/tokens is required
            if (string.IsNullOrEmpty(AccountKey) && string.IsNullOrEmpty(SharedAccessSignature))
            {
                results.Add(new ValidationResult($"The {nameof(AccountKey)} field or the {nameof(SharedAccessSignature)} field is required."));
            }

            return results;
        }

        internal void Log(ILogger logger)
        {
            logger.LogProviderOption(nameof(AccountKey), AccountKey, redact: true);
            logger.LogProviderOption(nameof(AccountUri), AccountUri);
            logger.LogProviderOption(nameof(BlobPrefix), BlobPrefix);
            logger.LogProviderOption(nameof(ContainerName), ContainerName);
            logger.LogProviderOption(nameof(SharedAccessSignature), SharedAccessSignature, redact: true);
        }

        
    }
}
