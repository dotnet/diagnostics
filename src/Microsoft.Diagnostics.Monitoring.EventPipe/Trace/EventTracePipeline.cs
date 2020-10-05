// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.Contracts;
using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class EventTracePipeline : EventSourcePipeline<EventTracePipelineSettings>
    {
        private readonly Func<Stream, CancellationToken, Task> _streamAvailable;
        public EventTracePipeline(DiagnosticsClient client, EventTracePipelineSettings settings, Func<Stream, CancellationToken, Task> streamAvailable)
            : base(client, settings)
        {
            _streamAvailable = streamAvailable ?? throw new ArgumentNullException(nameof(streamAvailable));
        }

        internal override DiagnosticsEventPipeProcessor CreateProcessor()
        {
            return new DiagnosticsEventPipeProcessor(PipeMode.Nettrace, configuration: Settings.Configuration, streamAvailable: _streamAvailable);
        }
    }
}
