// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal sealed class ActivitySourceData
    {
        public ActivitySourceData(
            string name,
            string? version)
        {
            Name = name;
            Version = version;
        }

        public string Name { get; }
        public string? Version { get; }
    }
}
