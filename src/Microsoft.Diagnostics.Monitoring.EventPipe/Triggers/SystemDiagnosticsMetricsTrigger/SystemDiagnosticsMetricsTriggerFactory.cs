// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.SystemDiagnosticsMetrics
{
    /// <summary>
    /// The trigger factory for the <see cref="SystemDiagnosticsMetricsTrigger"/>.
    /// </summary>
    internal sealed class SystemDiagnosticsMetricsTriggerFactory :
        ITraceEventTriggerFactory<SystemDiagnosticsMetricsTriggerSettings>
    {
        public ITraceEventTrigger Create(SystemDiagnosticsMetricsTriggerSettings settings)
        {
            return new SystemDiagnosticsMetricsTrigger(settings);
        }
    }
}
