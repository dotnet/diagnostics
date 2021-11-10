// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal interface ICountersLogger
    {
        //TODO Consider making these async.

        void Log(ICounterPayload counter);
        void PipelineStarted();
        void PipelineStopped();
    }
}
