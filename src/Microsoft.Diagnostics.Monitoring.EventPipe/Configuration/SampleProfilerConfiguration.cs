// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class SampleProfilerConfiguration : MonitoringSourceConfiguration
    {
        public override IList<EventPipeProvider> GetProviders() =>
            new EventPipeProvider[]
            {
                new EventPipeProvider(SampleProfilerProviderName, EventLevel.Informational)
            };

        public override int BufferSizeInMB => 1;

        public override bool RequestRundown
        {
            get => false;
            set => throw new NotSupportedException();
        }
    }
}
