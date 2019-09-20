// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tools.Counters.Exporters;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private int _processId;
        private int _interval;
        private List<string> _counterList;
        private CancellationToken _ct;
        private IConsole _console;
        private ICounterExporter exporter;
        private ConsoleWriter writer;
        private CounterFilter filter;
        private ulong _sessionId;
        private string _format;
        private string _output;
        private bool pauseCmdSet;

        public CounterMonitor()
        {
            writer = new ConsoleWriter();
            filter = new CounterFilter();
            pauseCmdSet = false;
        }

        private void DynamicAllMonitor(TraceEvent obj)
        {
            // If we are paused, ignore the event. 
            // There's a potential race here between the two tasks but not a huge deal if we miss by one event.
            writer.ToggleStatus(pauseCmdSet);

            if (obj.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                // If it's not a counter we asked for, ignore it.
                if (!filter.Filter(obj.ProviderName, payloadFields["Name"].ToString())) return;

                ICounterPayload payload = payloadFields["CounterType"].Equals("Sum") ? (ICounterPayload)new IncrementingCounterPayload(payloadFields, _interval) : (ICounterPayload)new CounterPayload(payloadFields);
                writer.Update(obj.ProviderName, payload, pauseCmdSet);
            }
        }

        // Writes out the counter data as a user-specified file format.
        private void DynamicAllExport(TraceEvent obj)
        {
            if (obj.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);
                if (obj.EventName.Equals("EventCounters"))
                {
                    // If it's not a counter we asked for, ignore it.
                    if (!filter.Filter(obj.ProviderName, payloadFields["Name"].ToString())) return;

                    // There really isn't a great way to tell whether an EventCounter payload is an instance of 
                    // IncrementingCounterPayload or CounterPayload, so here we check the number of fields 
                    // to distinguish the two.
                    ICounterPayload payload = payloadFields["CounterType"].Equals("Sum") ? (ICounterPayload)new IncrementingCounterPayload(payloadFields, _interval) : (ICounterPayload)new CounterPayload(payloadFields);
                    exporter.Write(obj.ProviderName, payload);
                }
            }
        }

        private void StopMonitor(bool liveMonitor)
        {
            try
            {
                EventPipeClient.StopTracing(_processId, _sessionId);
            }
            catch (EndOfStreamException ex)
            {
                // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                Debug.WriteLine($"[ERROR] {ex.ToString()}");
            }

            if (liveMonitor)
            {
                exporter.Flush();
            }
        }

        public async Task<int> Monitor(CancellationToken ct, List<string> counter_list, IConsole console, int processId, int refreshInterval)
        {
            try
            {
                _ct = ct;
                _counterList = counter_list; // NOTE: This variable name has an underscore because that's the "name" that the CLI displays. System.CommandLine doesn't like it if we change the variable to camelcase. 
                _console = console;
                _processId = processId;
                _interval = refreshInterval;

                return await Start(true);
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

        public async Task<int> Export(CancellationToken ct, List<string> counter_list, IConsole console, int processId, int refreshInterval, string format, string output)
        {
            try
            {
                _ct = ct;
                _counterList = counter_list; // NOTE: This variable name has an underscore because that's the "name" that the CLI displays. System.CommandLine doesn't like it if we change the variable to camelcase. 
                _console = console;
                _processId = processId;
                _interval = refreshInterval;
                _format = format;
                _output = output;

                return await Start(false);
            }
            catch (OperationCanceledException)
            {
            }

            return 1;
        }


        // Use EventPipe CollectTracing2 command to start monitoring. This may throw.
        private EventPipeEventSource RequestTracingV2(string providerString)
        {
            var configuration = new SessionConfigurationV2(
                                        circularBufferSizeMB: 1000,
                                        format: EventPipeSerializationFormat.NetTrace,
                                        requestRundown: false,
                                        providers: Trace.Extensions.ToProviders(providerString));
            var binaryReader = EventPipeClient.CollectTracing2(_processId, configuration, out _sessionId);
            return new EventPipeEventSource(binaryReader);
        }

        // Use EventPipe CollectTracing command to start monitoring. This may throw.
        private EventPipeEventSource RequestTracingV1(string providerString)
        {
            var configuration = new SessionConfiguration(
                                        circularBufferSizeMB: 1000,
                                        format: EventPipeSerializationFormat.NetTrace,
                                        providers: Trace.Extensions.ToProviders(providerString));
            var binaryReader = EventPipeClient.CollectTracing(_processId, configuration, out _sessionId);
            return new EventPipeEventSource(binaryReader);
        }

        private string buildProviderString()
        {
            string providerString;

            if (_counterList.Count == 0)
            {
                CounterProvider defaultProvider = null;
                _console.Out.WriteLine($"counter_list is unspecified. Monitoring all counters by default.");

                // Enable the default profile if nothing is specified
                if (!KnownData.TryGetProvider("System.Runtime", out defaultProvider))
                {
                    _console.Error.WriteLine("No providers or profiles were specified and there is no default profile available.");
                    return "";
                }
                providerString = defaultProvider.ToProviderString(_interval);
                filter.AddFilter("System.Runtime");
            }
            else
            {
                CounterProvider provider = null;
                StringBuilder sb = new StringBuilder("");
                for (var i = 0; i < _counterList.Count; i++)
                {
                    string counterSpecifier = _counterList[i];
                    string[] tokens = counterSpecifier.Split('[');
                    string providerName = tokens[0];
                    if (!KnownData.TryGetProvider(providerName, out provider))
                    {
                        sb.Append(CounterProvider.SerializeUnknownProviderName(providerName, _interval));
                    }
                    else
                    {
                        sb.Append(provider.ToProviderString(_interval));    
                    }
                    
                    if (i != _counterList.Count - 1)
                    {
                        sb.Append(",");
                    }

                    if (tokens.Length == 1)
                    {
                        filter.AddFilter(providerName); // This means no counter filter was specified.
                    }
                    else
                    {
                        string counterNames = tokens[1];
                        string[] enabledCounters = counterNames.Substring(0, counterNames.Length-1).Split(',');
                        
                        filter.AddFilter(providerName, enabledCounters);
                    }
                }
                providerString = sb.ToString();
            }
            return providerString;
        }
        

        private async Task<int> Start(bool liveMonitor)
        {
            if (_processId == 0)
            {
                _console.Error.WriteLine("ProcessId is required.");
                return 1;
            }

            // If we are exporting the counter data as file, do some more sanity checks.
            if (!liveMonitor)
            {
                if (_output.Length == 0)
                {
                    _console.Error.WriteLine("Output cannot be an empty string");
                    return 1;
                }
                if (_sortBy != "timestamp" && _sortBy != "counter")
                {
                    _console.Error.WriteLine($"Sorting by {_sortBy} is not supported.");
                    return 1;
                }

                if (_format == "csv")
                {
                    exporter = new CSVExporter();
                }
                else if (_format == "json")
                {
                    exporter = new JSONExporter();
                }
                else
                {
                    _console.Error.WriteLine($"The output format {_format} is not a valid output format.");
                }
                exporter.Initialize(_output, "TestProcessName");
            }

            string providerString = buildProviderString();

            ManualResetEvent shouldExit = new ManualResetEvent(false);
            _ct.Register(() => shouldExit.Set());

            var terminated = false;
            writer.AssignRowsAndInitializeDisplay();

            Task monitorTask = new Task(() => {
                try
                {
                    EventPipeEventSource source = RequestTracingV2(providerString);
                    if (liveMonitor)
                        source.Dynamic.All += DynamicAllMonitor;
                    else
                    {
                        Debug.Assert(exporter != null, "exporter object should not be null if we are exporting");
                        source.Dynamic.All += DynamicAllExport;
                    }
                    source.Process();
                }
                catch (EventPipeUnknownCommandException)
                {
                    // If unknown command exception is thrown, it's likely the app being monitored is running an older version of runtime that doesn't support CollectTracingV2. Try again with V1.
                    EventPipeEventSource source = RequestTracingV1(providerString);
                    if (liveMonitor)
                        source.Dynamic.All += DynamicAllMonitor;
                    else
                    {
                        Debug.Assert(exporter != null, "exporter object should not be null if we are exporting");
                        source.Dynamic.All += DynamicAllExport;
                    }
                    source.Process();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] {ex.ToString()}");
                }
                finally
                {
                    terminated = true; // This indicates that the runtime is done. We shouldn't try to talk to it anymore.
                    shouldExit.Set();
                }
            });

            monitorTask.Start();

            while(!shouldExit.WaitOne(250))
            {
                while (true)
                {
                    if (shouldExit.WaitOne(250))
                    {
                        StopMonitor(liveMonitor);
                        return 0;
                    }
                    if (Console.KeyAvailable)
                    {
                        break;
                    }
                }
                ConsoleKey cmd = Console.ReadKey(true).Key;
                if (cmd == ConsoleKey.Q)
                {
                    if (!liveMonitor)
                    {
                        exporter.Flush();
                    }
                    break;
                }
                else if (cmd == ConsoleKey.P)
                {
                    pauseCmdSet = true;
                }
                else if (cmd == ConsoleKey.R)
                {
                    pauseCmdSet = false;
                }
            }
            if (!terminated)
            {
                StopMonitor(liveMonitor);
            }
            
            return await Task.FromResult(0);
        }
    }
}
