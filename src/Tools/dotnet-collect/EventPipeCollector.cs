using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public class EventPipeCollector : EventCollector
    {
        private readonly string _configPath;

        public EventPipeCollector(CollectionConfiguration config, string configPath) : base(config) => _configPath = configPath;

        public override Task StartCollectingAsync()
        {
            var configContent = Config.ToConfigString();
            return File.WriteAllTextAsync(_configPath, configContent);
        }

        public override Task StopCollectingAsync()
        {
            File.Delete(_configPath);
            return Task.CompletedTask;
        }

        protected override StackSource GetStackSource(SymbolReader symbolReader)
        {
            var netperfFilePath = GetNetPerfFilePath();
            var etlxFilePath = TraceLog.CreateFromEventPipeDataFile(netperfFilePath);

            var eventLog = new TraceLog(etlxFilePath);

            try
            {
                var stackSource = new MutableTraceEventStackSource(eventLog)
                {
                    OnlyManagedCodeStacks = true // EventPipe currently only has managed code stacks.
                };

                var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);
                computer.GenerateThreadTimeStacks(stackSource);

                return stackSource;
            }
            finally
            {
                eventLog.Dispose();

                if (File.Exists(etlxFilePath))
                {
                    File.Delete(etlxFilePath);
                }
            }
        }

        private string GetNetPerfFilePath()
        {
            var processName = Path.GetFileNameWithoutExtension(ConfigPathDetector.TryDetectConfigPath(Config.ProcessId.Value))
                ?? Path.GetFileNameWithoutExtension(_configPath);

            return Path.Combine(Config.OutputPath, $"{processName}.{Config.ProcessId.Value}.netperf");
        }
    }
}
