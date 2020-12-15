// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
            scope.Values.Add("TargetProcessId", endpointInfo.ProcessId);
            scope.Values.Add("TargetRuntimeInstanceCookie", endpointInfo.RuntimeInstanceCookie);
        }
    }
}
