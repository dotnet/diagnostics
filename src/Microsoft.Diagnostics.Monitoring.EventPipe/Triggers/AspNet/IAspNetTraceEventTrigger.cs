// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal enum AspnetTriggerEventType
    {
        Start,
        Stop,
        Heartbeat
    }

    /// <summary>
    /// This interface is for testing. It allows the trigger to be callable with a TraceEvent,
    /// or with a deconstruction of the TraceEvent payload while running tests.
    /// </summary>
    internal interface IAspNetTraceEventTrigger
    {
        bool HasSatisfiedCondition(DateTime timestamp, AspnetTriggerEventType eventType, string activityId, string path, int? statusCode, long? duration);
    }
}
