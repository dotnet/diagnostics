using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public abstract class EventCollector
    {
        public abstract Task StartCollectingAsync();
        public abstract Task StopCollectingAsync();
    }
}
