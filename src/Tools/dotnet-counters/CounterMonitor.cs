using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private string configPath;  // Path to the eventpipe config file that needs to be generated
        private EventPipeCollector collector;
        private CollectionConfiguration config;

        private int _processId;
        private int _interval;
        private string _counterList;
        private CancellationToken _ct;
        private IConsole _console;

        public CounterMonitor()
        {
        }

        public async Task<int> Monitor(CancellationToken ct, string counterList, IConsole console, int processId, int interval)
        {
            try
            {
                _ct = ct;
                _counterList = counterList;
                _console = console;
                _processId = processId;
                _interval = interval;

                return await StartMonitor();
            }

            catch (OperationCanceledException)
            {
                await collector.StopCollectingAsync();
                console.Out.WriteLine($"Tracing stopped. Trace files written to {config.OutputPath}");
                console.Out.WriteLine($"Complete");
                return 1;
            }
        }

        private async Task<int> StartMonitor()
        {
            if (_processId == 0) {
                _console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            if (_interval == 0) {
                _console.Error.WriteLine("interval is required.");
                return 1;
            }

            configPath = ConfigPathDetector.TryDetectConfigPath(_processId);

            if(string.IsNullOrEmpty(configPath))
            {
                _console.Error.WriteLine("Couldn't determine the path for the eventpipeconfig file from the process ID.");
                return 1;
            }

            _console.Out.WriteLine($"Detected config file path: {configPath}");

            config = new CollectionConfiguration()
            {
                ProcessId = _processId,
                CircularMB = 1000,  // TODO: Make this configurable?
                OutputPath = Directory.GetCurrentDirectory(),
                Interval = _interval
            };

            if (string.IsNullOrEmpty(_counterList))
            {
                _console.Out.WriteLine($"counter_list is unspecified. Monitoring all counters by default.");

                // Enable the default profile if nothing is specified
                if (!KnownData.TryGetProvider("System.Runtime", out var defaultProvider))
                {
                    _console.Error.WriteLine("No providers or profiles were specified and there is no default profile available.");
                    return 1;
                }
                config.AddProvider(defaultProvider);
            }

            if (File.Exists(configPath))
            {
                _console.Error.WriteLine("Config file already exists, tracing is already underway by a different consumer.");
                return 1;
            }

            collector = new EventPipeCollector(config, configPath);

            // Write the config file contents
            await collector.StartCollectingAsync();
            _console.Out.WriteLine("Tracing has started. Press Ctrl-C to stop.");
            await Task.Delay(int.MaxValue, _ct);
            return 0;
        }
    }
}
