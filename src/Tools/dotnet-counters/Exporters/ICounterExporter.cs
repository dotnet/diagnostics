namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    public interface ICounterExporter
    {
        void Write(string providerName, ICounterPayload counterPayload);
        void Initialize(string _output, string processName);
        void Flush();
    }
}
