namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    interface ICounterExporter
    {
        public void Write(string providerName, ICounterPayload counterPayload);
        public void Initialize(string _output, string processName);
        public void Flush();
    }
}
