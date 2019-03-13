using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        string configPath;  // Path to the eventpipe config file that needs to be generated

        public CounterMonitor()
        {
        }

        public async Task<int> Monitor(string counterList, IConsole console, int ProcessId, int interval)
        {
            if (ProcessId == 0) {
                console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            if (interval == 0) {
                console.Error.WriteLine("interval is required.");
                return 1;
            }

            console.Out.WriteLine($"processId: {ProcessId}, interval: {interval}, counterList: {counterList}");

            configPath = ConfigPathDetector.TryDetectConfigPath(ProcessId);

            if(string.IsNullOrEmpty(configPath))
            {
                console.Error.WriteLine("Couldn't determine the path for the eventpipeconfig file from the process ID. Specify the '--config-path' option to provide it manually.");
                return 1;
            }

            console.Out.WriteLine($"Detected config file path: {configPath}");

            var config = new CollectionConfiguration()
            {
                ProcessId = ProcessId,
                CircularMB = 1000,  // TODO: Make this configurable?
                OutputPath = Directory.GetCurrentDirectory(),
                Interval = interval
            };

            if (string.IsNullOrEmpty(counterList))
            {
                console.Out.WriteLine($"counter_list is unspecified. Monitoring all counters by default.");

                // Enable the default profile if nothing is specified
                if (!KnownData.TryGetProvider("System.Runtime", out var defaultProvider))
                {
                    console.Error.WriteLine("No providers or profiles were specified and there is no default profile available.");
                    return 1;
                }
                config.AddProvider(defaultProvider);
            }

            if (File.Exists(configPath))
            {
                console.Error.WriteLine("Config file already exists, tracing is already underway by a different consumer.");
                return 1;
            }

            EventPipeCollector collector = new EventPipeCollector(config, configPath);

            // Write the config file contents
            await collector.StartCollectingAsync();
            console.Out.WriteLine("Tracing has started. Press Ctrl-C to stop.");

            // await WaitForCtrlCAsync(console);
            Thread.Sleep(20000);

            await collector.StopCollectingAsync();
            console.Out.WriteLine($"Tracing stopped. Trace files written to {config.OutputPath}");

            console.Out.WriteLine($"Complete");
            return 0;
        }

    }
}
