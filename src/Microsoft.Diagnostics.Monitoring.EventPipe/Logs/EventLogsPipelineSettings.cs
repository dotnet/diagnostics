// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventLogsPipelineSettings : EventSourcePipelineSettings
    {
        // The default log level for all categories
        public LogLevel LogLevel { get; set; } = LogLevel.Trace;

        // The logger categories and levels at which log entries are collected.
        public IDictionary<string, LogLevel?> FilterSpecs { get; set; }

        // This setting will collect logs for the application-defined categories and levels.
        public bool UseAppFilters { get; set; } = true;
    }
}
