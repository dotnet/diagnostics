// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
