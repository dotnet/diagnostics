// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.Pipelines
{
    internal sealed class EventPipeTriggerPipelineSettings<TSettings> :
        EventSourcePipelineSettings
    {
        /// <summary>
        /// The event pipe configuration used to collect trace event information for the trigger
        /// to use to determine if the trigger condition is satisfied.
        /// </summary>
        public MonitoringSourceConfiguration Configuration { get; set; }

        /// <summary>
        /// The factory that produces the trigger instantiation.
        /// </summary>
        public ITraceEventTriggerFactory<TSettings> TriggerFactory { get; set; }

        /// <summary>
        /// The settings to pass to the trigger factory when creating the trigger.
        /// </summary>
        public TSettings TriggerSettings { get; set; }
    }
}
