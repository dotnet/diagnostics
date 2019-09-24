namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    public interface ICounterRenderer
    {
		void Initialize();
		void EventPipeSourceConnected();
		void ToggleStatus(bool paused);
		void CounterPayloadReceived(string providerName, ICounterPayload payload, bool paused);
		void Stop();
    }
}
