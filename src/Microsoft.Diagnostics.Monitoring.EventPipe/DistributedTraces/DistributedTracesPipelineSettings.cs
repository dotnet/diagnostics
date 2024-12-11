// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class DistributedTracesPipelineSettings : EventSourcePipelineSettings
    {
        public double SamplingRatio { get; set; } = 1.0D;

        public string[]? Sources { get; set; }
    }
}
