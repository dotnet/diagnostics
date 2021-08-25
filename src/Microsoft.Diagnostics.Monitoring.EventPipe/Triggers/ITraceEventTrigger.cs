// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers
{
    /// <summary>
    /// Interface for all <see cref="TraceEvent"/>-based triggers.
    /// </summary>
    internal interface ITraceEventTrigger
    {
        /// <summary>
        /// Mapping of event providers to event names in which the trigger has an interest.
        /// </summary>
        /// <remarks>
        /// The method may return null to signify that all events can be forwarded to the trigger.
        /// Each event provider entry also may have a null or empty list of event names to
        /// signify that all events from the provider can be forwarded to the trigger.
        /// </remarks>
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetProviderEventMap();

        /// <summary>
        /// Check if the given <see cref="TraceEvent"/> satisfies the condition
        /// described by the trigger.
        /// </summary>
        bool HasSatisfiedCondition(TraceEvent traceEvent);
    }
}
