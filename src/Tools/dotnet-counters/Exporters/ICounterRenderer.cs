// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    public interface ICounterRenderer
    {
        void Initialize();
        void EventPipeSourceConnected();
        void ToggleStatus(bool paused);
        void CounterPayloadReceived(CounterPayload payload, bool paused);
        void SetErrorText(string errorText);
        void Stop();
    }
}
