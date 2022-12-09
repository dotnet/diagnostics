// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Monitoring.EventPipe;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    internal interface ICounterRenderer
    {
        void Initialize(); //Maps to started?
        void EventPipeSourceConnected(); // PipelineStarted
        void ToggleStatus(bool paused); //Occurs every event
        void CounterPayloadReceived(CounterPayload payload, bool paused);
        void CounterStopped(CounterPayload payload);
        void SetErrorText(string errorText);
        void Stop(); //Maps to pipeline stopped
    }
}
