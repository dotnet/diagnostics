// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventLogsPipeline : EventSourcePipeline<EventLogsPipelineSettings>
    {
        private readonly ILoggerFactory _factory;
        public EventLogsPipeline(DiagnosticsClient client, EventLogsPipelineSettings settings, ILoggerFactory factory) 
            : base(client, settings)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal override DiagnosticsEventPipeProcessor CreateProcessor()
        {
            return new DiagnosticsEventPipeProcessor(PipeMode.Logs, loggerFactory: _factory, logsLevel: Settings.LogLevel);
        }
    }
}
