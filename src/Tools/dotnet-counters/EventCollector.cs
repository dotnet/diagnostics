using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public abstract class EventCollector
    {
        public abstract Task StartCollectingAsync();
        public abstract Task StopCollectingAsync();
    }
}
