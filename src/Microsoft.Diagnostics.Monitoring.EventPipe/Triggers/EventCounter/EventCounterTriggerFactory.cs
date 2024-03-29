﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.EventCounter
{
    /// <summary>
    /// The trigger factory for the <see cref="EventCounterTrigger"/>.
    /// </summary>
    internal sealed class EventCounterTriggerFactory :
        ITraceEventTriggerFactory<EventCounterTriggerSettings>
    {
        public ITraceEventTrigger Create(EventCounterTriggerSettings settings)
        {
            return new EventCounterTrigger(settings);
        }
    }
}
