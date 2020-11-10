// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.Egress.FileSystem
{
    /// <summary>
    /// Egress provider options for file system egress.
    /// </summary>
    internal class FileSystemEgressProviderOptions :
        EgressProviderOptions
    {
        /// <summary>
        /// The directory path to which the stream data will be egressed.
        /// </summary>
        [Required]
        public string DirectoryPath { get; set; }

        /// <summary>
        /// The directory path to which the stream data will initially be written, if specified; the file will then
        /// be moved/renamed to the directory specified in <see cref="FileSystemEgressProviderOptions.DirectoryPath"/>.
        /// </summary>
        public string IntermediateDirectoryPath { get; set; }
    }
}
