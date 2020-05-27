// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
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
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private int _processId;
        private int _interval;
        private List<string> _counterList;
        private CancellationToken _ct;
        private IConsole _console;
        private ICounterRenderer _renderer;
        private CounterFilter filter;
        private string _output;
        private bool pauseCmdSet;
        private DiagnosticsClient _diagnosticsClient;
        private EventPipeSession _session;

        public CounterMonitor()
        {
            filter = new CounterFilter();
            pauseCmdSet = false;
        }

        private void DynamicAllMonitor(TraceEvent obj)
        {
            // If we are paused, ignore the event. 
            // There's a potential race here between the two tasks but not a huge deal if we miss by one event.
            _renderer.ToggleStatus(pauseCmdSet);

            if (obj.EventName.Equals("EventCounters"))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                // If it's not a counter we asked for, ignore it.
                if (!filter.Filter(obj.ProviderName, payloadFields["Name"].ToString())) return;

                ICounterPayload payload = payloadFields["CounterType"].Equals("Sum") ? (ICounterPayload)new IncrementingCounterPayload(payloadFields, _interval) : (ICounterPayload)new CounterPayload(payloadFields);
                _renderer.CounterPayloadReceived(obj.ProviderName, payload, pauseCmdSet);
            }
        }

        private void StopMonitor()
        {
            try
            {
                _session.Stop();
            }
            catch (EndOfStreamException ex)
            {
                // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                Debug.WriteLine($"[ERROR] {ex.ToString()}");
            }
            // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
            catch (TimeoutException)
            {
            }
            // On Unix platforms, we may actually get a PNSE since the pipe is gone with the process, and Runtime Client Library
            // does not know how to distinguish a situation where there is no pipe to begin with, or where the process has exited
            // before dotnet-counters and got rid of a pipe that once existed.
            // Since we are catching this in StopMonitor() we know that the pipe once existed (otherwise the exception would've 
            // been thrown in StartMonitor directly)
            catch (PlatformNotSupportedException)
            {
            }
            _renderer.Stop();
        }

        public async Task<int> Monitor(CancellationToken ct, List<string> counter_list, IConsole console, int processId, int refreshInterval, string name)
        {
            if (name != null)
            {
                if (processId != 0)
                {
                    Console.WriteLine("Can only specify either --name or --process-id option.");
                    return 0;
                }
                processId = CommandUtils.FindProcessIdWithName(name);
                if (processId < 0)
                {
                    return 0;
                }
            }

            try
            {
                _ct = ct;
                _counterList = counter_list; // NOTE: This variable name has an underscore because that's the "name" that the CLI displays. System.CommandLine doesn't like it if we change the variable to camelcase. 
                _console = console;
                _processId = processId;
                _interval = refreshInterval;
                _renderer = new ConsoleWriter();
                _diagnosticsClient = new DiagnosticsClient(processId);

                return await Start();
            }

            catch (OperationCanceledException)
            {
                try
                {
                    _session.Stop();
                }
                catch (Exception) {} // Swallow all exceptions for now.
                
                console.Out.WriteLine($"Complete");
                return 1;
            }
        }

        public async Task<int> Collect(CancellationToken ct, List<string> counter_list, IConsole console, int processId, int refreshInterval, CountersExportFormat format, string output, string name)
        {
            if (name != null)
            {
                if (processId != 0)
                {
                    Console.WriteLine("Can only specify either --name or --process-id option.");
                    return 0;
                }
                processId = CommandUtils.FindProcessIdWithName(name);
                if (processId < 0)
                {
                    return 0;
                }
            }

            try
            {
                _ct = ct;
                _counterList = counter_list; // NOTE: This variable name has an underscore because that's the "name" that the CLI displays. System.CommandLine doesn't like it if we change the variable to camelcase. 
                _console = console;
                _processId = processId;
                _interval = refreshInterval;
                _output = output;
                _diagnosticsClient = new DiagnosticsClient(processId);

                if (_output.Length == 0)
                {
                    _console.Error.WriteLine("Output cannot be an empty string");
                    return 0;
                }

                if (format == CountersExportFormat.csv)
                {
                    _renderer = new CSVExporter(output);
                }
                else if (format == CountersExportFormat.json)
                {
                    // Try getting the process name.
                    string processName = "";
                    try
                    {
                        processName = Process.GetProcessById(_processId).ProcessName;
                    }
                    catch (Exception) { }
                    _renderer = new JSONExporter(output, processName); ;
                }
                else
                {
                    _console.Error.WriteLine($"The output format {format} is not a valid output format.");
                    return 0;
                }
                return await Start();
            }
            catch (OperationCanceledException)
            {
            }

            return 1;
        }

        private string BuildProviderString()
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
        

        private async Task<int> Start()
        {
            if (_processId == 0)
            {
                _console.Error.WriteLine("--process-id is required.");
                return 1;
            }

            string providerString = BuildProviderString();
            if (providerString.Length == 0)
            {
                return 1;
            }

            _renderer.Initialize();

            ManualResetEvent shouldExit = new ManualResetEvent(false);
            _ct.Register(() => shouldExit.Set());
            Task monitorTask = new Task(() => {
                try
                {
                    _session = _diagnosticsClient.StartEventPipeSession(Trace.Extensions.ToProviders(providerString), false, 10);
                    var source = new EventPipeEventSource(_session.EventStream);
                    source.Dynamic.All += DynamicAllMonitor;
                    _renderer.EventPipeSourceConnected();
                    source.Process();
                }
                catch (DiagnosticsClientException ex)
                {
                    Console.WriteLine($"Failed to start the counter session: {ex.ToString()}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] {ex.ToString()}");
                }
                finally
                {
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
                        StopMonitor();
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
                    StopMonitor();
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

            return await Task.FromResult(0);
        }
    }
}
