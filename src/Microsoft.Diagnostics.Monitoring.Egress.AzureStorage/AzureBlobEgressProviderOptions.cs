// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    /// <summary>
    /// Egress provider options for Azure blob storage.
    /// </summary>
    internal class AzureBlobEgressProviderOptions :
        EgressProviderOptions,
        IValidatableObject
    {
        /// <summary>
        /// The URI of the Azure blob storage account.
        /// </summary>
        [Required]
        public Uri AccountUri { get; set; }

        /// <summary>
        /// The acount key used to access the Azure blob storage account.
        /// </summary>
        /// <remarks>
        /// If not provided, <see cref="AzureBlobEgressProviderOptions.SharedAccessSignature"/> must be specified.
        /// </remarks>
        public string AccountKey { get; set; }

        /// <summary>
        /// The shared access signature (SAS) used to access the azure blob storage account.
        /// </summary>
        /// <remarks>
        /// If not provided, <see cref="AzureBlobEgressProviderOptions.AccountKey"/> must be specified.
        /// </remarks>
        public string SharedAccessSignature { get; set; }

        /// <summary>
        /// The name of the container to which the blob will be egressed. If egressing to the root container,
        /// use the "$root" sentinel value.
        /// </summary>
        [Required]
        public string ContainerName { get; set; }

        /// <summary>
        /// The prefix to prepend to the blob name.
        /// </summary>
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
    }
}
