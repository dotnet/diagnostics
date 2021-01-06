// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class KeyValueLogScopeExtensions
    {
        public static void AddArtifactType(this KeyValueLogScope scope, string artifactType)
        {
            scope.Values.Add("ArtifactType", artifactType);
        }

        public static void AddEndpointInfo(this KeyValueLogScope scope, IEndpointInfo endpointInfo)
        {
            scope.Values.Add(
                ArtifactMetadataNames.ArtifactSource.ProcessId,
                endpointInfo.ProcessId.ToString(CultureInfo.InvariantCulture));
            scope.Values.Add(
                ArtifactMetadataNames.ArtifactSource.RuntimeInstanceCookie,
                endpointInfo.RuntimeInstanceCookie.ToString("N"));
        }
    }
}
