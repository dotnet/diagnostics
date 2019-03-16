using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public class EventPipeCollector : EventCollector
    {
        private readonly CollectionConfiguration _config;
        private readonly string _configPath;

        public EventPipeCollector(CollectionConfiguration config, string configPath)
        {
            _config = config;
            _configPath = configPath;
        }

        public override Task StartCollectingAsync()
        {
            var configContent = _config.ToConfigString();
            return File.WriteAllTextAsync(_configPath, configContent);
        }

        public override Task StopCollectingAsync()
        {
            File.Delete(_configPath);
            return Task.CompletedTask;
        }
    }
}
