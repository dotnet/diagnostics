﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventProcessInfoPipelineSettings : EventSourcePipelineSettings
    {
        public Func<string, CancellationToken, Task> CommandLineCallback { get; set; }
    }

}
