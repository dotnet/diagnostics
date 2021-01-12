// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// Metadata keys that represent artfiact information.
    /// </summary>
    internal static class ArtifactMetadataNames
    {
        /// <summary>
        /// Represents the type of artifact created from the source.
        /// </summary>
        public const string ArtifactType = nameof(ArtifactType);

        /// <summary>
        /// Metadata keus that represent the source of an artifact.
        /// </summary>
        public static class ArtifactSource
        {
            /// <summary>
            /// The ID of the process from which the artifact was collected.
            /// </summary>
            public const string ProcessId = nameof(ArtifactSource) + "_" + nameof(ProcessId);

            /// <summary>
            /// The runtime instance cookie of the process from which the artifact was collected.
            /// </summary>
            public const string RuntimeInstanceCookie = nameof(ArtifactSource) + "_" + nameof(RuntimeInstanceCookie);
        }
    }
}
