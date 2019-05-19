namespace Microsoft.Diagnostics.Tools.Counters
{
    public interface IOutputWriter
    {
        void Update(string providerName, ICounterPayload payload);
    }
}
