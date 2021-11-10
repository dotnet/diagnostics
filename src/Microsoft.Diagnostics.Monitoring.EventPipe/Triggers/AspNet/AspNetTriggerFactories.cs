// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestCountTriggerFactory : ITraceEventTriggerFactory<AspNetRequestCountTriggerSettings>
    {
        public ITraceEventTrigger Create(AspNetRequestCountTriggerSettings settings) => new AspNetRequestCountTrigger(settings);
    }

    internal sealed class AspNetRequestDurationTriggerFactory : ITraceEventTriggerFactory<AspNetRequestDurationTriggerSettings>
    {
        public ITraceEventTrigger Create(AspNetRequestDurationTriggerSettings settings) => new AspNetRequestDurationTrigger(settings);
    }

    internal sealed class AspNetRequestStatusTriggerFactory : ITraceEventTriggerFactory<AspNetRequestStatusTriggerSettings>
    {
        public ITraceEventTrigger Create(AspNetRequestStatusTriggerSettings settings) => new AspNetRequestStatusTrigger(settings);
    }
}
