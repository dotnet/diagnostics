// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.CommandLine;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;

using Microsoft.Diagnostics.Tools.RuntimeClient.Eventing;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private string outputPath;
        private ulong sessionId;

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
                try
                {
                    EventPipeClient.DisableTracingToFile(_processId, sessionId);    
                }
                catch (Exception) {} // Swallow all exceptions for now.
                
                console.Out.WriteLine($"Tracing stopped. Trace files written to {outputPath}");
                console.Out.WriteLine($"Complete");
                return 1;
            }
        }

        private static IEnumerable<Provider> ToProviders(string providers)
        {
            if (string.IsNullOrWhiteSpace(providers))
                throw new ArgumentNullException(nameof(providers));
            return providers.Split(',')
                .Select(Provider.ToProvider);
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

            outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"dotnet-counters-{_processId}.netperf"); // TODO: This can be removed once events can be streamed in real time.

            CounterProvider defaultProvider = null;
            if (string.IsNullOrEmpty(_counterList))
            {
                _console.Out.WriteLine($"counter_list is unspecified. Monitoring all counters by default.");

                // Enable the default profile if nothing is specified
                if (!KnownData.TryGetProvider("System.Runtime", out defaultProvider))
                {
                    _console.Error.WriteLine("No providers or profiles were specified and there is no default profile available.");
                    return 1;
                }
            }
            // TODO: Parse _counterList and add appropriate "known" counters into a comma-separated list of providerString
            
            var providerString = defaultProvider.ToProviderString(_interval);

            var configuration = new SessionConfiguration(
                1000,
                0,
                outputPath,
                ToProviders(providerString));

            sessionId = EventPipeClient.EnableTracingToFile(_processId, configuration);

            // Write the config file contents
            _console.Out.WriteLine("Tracing has started. Press Ctrl-C to stop.");
            await Task.Delay(int.MaxValue, _ct);
            return 0;
        }
    }
}
