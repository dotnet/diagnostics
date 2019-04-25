// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private int _processId;
        private int _interval;
        private string _counterList;
        private CancellationToken _ct;
        private IConsole _console;
        private ConsoleWriter writer;
        private ulong _sessionId;
        public CounterMonitor()
        {
            writer = new ConsoleWriter();
        }

        private void Dynamic_All(TraceEvent obj)
        {
            if (obj.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                // There really isn't a great way to tell whether an EventCounter payload is an instance of 
                // IncrementingCounterPayload or CounterPayload, so here we check the number of fields 
                // to distinguish the two.                
                ICounterPayload payload = payloadFields["CounterType"] == "Sum" ? (ICounterPayload)new IncrementingCounterPayload(payloadFields) : (ICounterPayload)new CounterPayload(payloadFields);
                
                writer.Update(obj.ProviderName, payload);
            }
        }

        public async Task<int> Monitor(CancellationToken ct, string counter_list, IConsole console, int processId, int interval)
        {
            try
            {
                _ct = ct;
                _counterList = counter_list; // NOTE: This variable name has an underscore because that's the "name" that the CLI displays. System.CommandLine doesn't like it if we change the variable to camelcase. 
                _console = console;
                _processId = processId;
                _interval = interval;

                return await StartMonitor();
            }

            catch (OperationCanceledException)
            {
                try
                {
                    EventPipeClient.StopTracing(_processId, _sessionId);    
                }
                catch (Exception) {} // Swallow all exceptions for now.
                
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

            String providerString;

            if (string.IsNullOrEmpty(_counterList))
            {
                CounterProvider defaultProvider = null;
                _console.Out.WriteLine($"counter_list is unspecified. Monitoring all counters by default.");

                // Enable the default profile if nothing is specified
                if (!KnownData.TryGetProvider("System.Runtime", out defaultProvider))
                {
                    _console.Error.WriteLine("No providers or profiles were specified and there is no default profile available.");
                    return 1;
                }
                providerString = defaultProvider.ToProviderString(_interval);
            }
            else
            {
                string[] counters = _counterList.Split(" ");
                CounterProvider provider = null;
                StringBuilder sb = new StringBuilder("");
                for (var i = 0; i < counters.Length; i++)
                {
                    if (!KnownData.TryGetProvider(counters[i], out provider))
                    {
                        _console.Error.WriteLine($"No known provider called {counters[i]}.");
                        return 1;
                    }
                    sb.Append(provider.ToProviderString(_interval));
                    if (i != counters.Length - 1)
                    {
                        sb.Append(",");
                    }
                }
                providerString = sb.ToString();
            }
            Task monitorTask = new Task(() => {
                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: 1000,
                    outputPath: "",
                    providers: Trace.Extensions.ToProviders(providerString));

                var binaryReader = EventPipeClient.CollectTracing(_processId, configuration, out _sessionId);
                EventPipeEventSource source = new EventPipeEventSource(binaryReader);
                writer.InitializeDisplay();
                source.Dynamic.All += Dynamic_All;
                source.Process();
            });

            monitorTask.Start();
            await monitorTask;
            EventPipeClient.StopTracing(_processId, _sessionId);

            Task.FromResult(0);
            return 0;
        }
    }
}
