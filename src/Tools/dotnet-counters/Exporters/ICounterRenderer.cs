// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    public interface ICounterRenderer
    {
        void Initialize();
        void EventPipeSourceConnected();
        void ToggleStatus(bool paused);
        void CounterPayloadReceived(CounterPayload payload, bool paused);
        void CounterStopped(CounterPayload payload);
        void SetErrorText(string errorText);
        void Stop();
    }
}
