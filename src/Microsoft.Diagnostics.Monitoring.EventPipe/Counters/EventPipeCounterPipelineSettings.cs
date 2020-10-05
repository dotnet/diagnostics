// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class EventPipeCounterPipelineSettings : EventSourcePipelineSettings
    {
        public EventPipeCounterGroup[] CounterGroups { get; set; }
        public TimeSpan RefreshInterval { get; set; }
    }

    public class EventPipeCounterGroup
    {
        public string ProviderName { get; set; }
        public string[] CounterNames { get; set; }
    }
}
