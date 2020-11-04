// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Diagnostics.Monitoring.Egress.FileSystem
{
    internal class FileSystemEgressProviderOptions :
        EgressProviderOptions
    {
        [Required]
        public string DirectoryPath { get; set; }

        public bool UseIntermediateFile { get; set; }

        internal void Log(ILogger logger)
        {
            logger.LogProviderOption(nameof(DirectoryPath), DirectoryPath);
            logger.LogProviderOption(nameof(UseIntermediateFile), UseIntermediateFile);
        }
    }
}
