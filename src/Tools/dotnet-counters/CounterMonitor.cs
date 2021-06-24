// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Internal.Common.Utils;
using System.CommandLine.IO;

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
        private ManualResetEvent shouldExit;
        private bool _resumeRuntime;
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
                _session?.Stop();
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
            // On non-abrupt exits, the socket may be already closed by the runtime and we won't be able to send a stop request through it. 
            catch (ServerNotAvailableException)
            {
            }
            _renderer.Stop();
        }

        public async Task<int> Monitor(CancellationToken ct, List<string> counter_list, string counters, IConsole console, int processId, int refreshInterval, string name, string diagnosticPort, bool resumeRuntime)
        {
            if (!ProcessLauncher.Launcher.HasChildProc && !CommandUtils.ValidateArgumentsForAttach(processId, name, diagnosticPort, out _processId))
            {
                return ReturnCode.ArgumentError;
            }
            shouldExit = new ManualResetEvent(false);
            _ct.Register(() => shouldExit.Set());

            DiagnosticsClientBuilder builder = new DiagnosticsClientBuilder("dotnet-counters", 10);
            using (DiagnosticsClientHolder holder = await builder.Build(ct, _processId, diagnosticPort, showChildIO: false, printLaunchCommand: false))
            {
                if (holder == null)
                {
                    return ReturnCode.Ok;
                }
                try
                {
                    InitializeCounterList(counters, counter_list);
                    _ct = ct;
                    _console = console;
                    _interval = refreshInterval;
                    _renderer = new ConsoleWriter();
                    _diagnosticsClient = holder.Client;
                    _resumeRuntime = resumeRuntime;
                    int ret = await Start();
                    ProcessLauncher.Launcher.Cleanup();
                    return ret;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        _session.Stop();
                    }
                    catch (Exception) { } // Swallow all exceptions for now.

                    console.Out.WriteLine($"Complete");
                    return ReturnCode.Ok;
                }
            }
        }


        public async Task<int> Collect(CancellationToken ct, List<string> counter_list, string counters, IConsole console, int processId, int refreshInterval, CountersExportFormat format, string output, string name, string diagnosticPort, bool resumeRuntime)
        {
            if (!ProcessLauncher.Launcher.HasChildProc && !CommandUtils.ValidateArgumentsForAttach(processId, name, diagnosticPort, out _processId))
            {
                return ReturnCode.ArgumentError;
            }

            shouldExit = new ManualResetEvent(false);
            _ct.Register(() => shouldExit.Set());

            DiagnosticsClientBuilder builder = new DiagnosticsClientBuilder("dotnet-counters", 10);
            using (DiagnosticsClientHolder holder = await builder.Build(ct, _processId, diagnosticPort, showChildIO: false, printLaunchCommand: false))
            {
                if (holder == null)
                {
                    return ReturnCode.Ok;
                }

                try
                {
                    InitializeCounterList(counters, counter_list);
                    _ct = ct;
                    _console = console;
                    _interval = refreshInterval;
                    _output = output;
                    _diagnosticsClient = holder.Client;
                    if (_output.Length == 0)
                    {
                        _console.Error.WriteLine("Output cannot be an empty string");
                        return ReturnCode.ArgumentError;
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
                            if (ProcessLauncher.Launcher.HasChildProc)
                            {
                                _processId = ProcessLauncher.Launcher.ChildProc.Id;
                            }
                            processName = Process.GetProcessById(_processId).ProcessName;
                        }
                        catch (Exception) { }
                        _renderer = new JSONExporter(output, processName);
                    }
                    else
                    {
                        _console.Error.WriteLine($"The output format {format} is not a valid output format.");
                        return ReturnCode.ArgumentError;
                    }
                    int ret = await Start();
                    return ret;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        _session.Stop();
                    }
                    catch (Exception) { } // session.Stop() can throw if target application already stopped before we send the stop command.
                    return ReturnCode.Ok;
                }
            }
        }

        private void InitializeCounterList(string counters, List<string> counterList)
        {
            if (_processId != 0)
            {
                GenerateCounterList(counters, counterList);
                _counterList = counterList;
            }
            else
            {
                _counterList = GenerateCounterList(counters);
            }
        }

        internal List<string> GenerateCounterList(string counters)
        {
            List<string> counterList = new List<string>();
            bool inParen = false;
            int startIdx = -1;
            for (int i = 0; i < counters.Length; i++)
            {
                if (!inParen)
                {
                    if (counters[i] == '[')
                    {
                        inParen = true;
                        continue;
                    }
                    else if (counters[i] == ',')
                    {
                        counterList.Add(counters.Substring(startIdx, i - startIdx));
                        startIdx = -1;
                    }
                    else if (startIdx == -1 && counters[i] != ' ')
                    {
                        startIdx = i;
                    }
                }
                else if (inParen && counters[i] == ']')
                {
                    inParen = false;
                }
            }
            counterList.Add(counters.Substring(startIdx, counters.Length - startIdx));
            return counterList;
        }

        /// <summary>
        /// This gets invoked by Collect/Monitor when user specifies target process (instead of launching at startup)
        /// The user may specify --counters option as well as the default list of counters, so we try to merge it here.
        /// </summary>
        /// <param name="counters"></param>
        /// <param name="counter_list"></param>
        internal void GenerateCounterList(string counters, List<string> counter_list)
        {
            List<string> counterOptionList = GenerateCounterList(counters);
            foreach (string counter in counterOptionList)
            {
                if (!counter_list.Contains(counter))
                {
                    counter_list.Add(counter);
                }
            }
        }

        private string BuildProviderString()
        {
            string providerString;
            if (_counterList.Count == 0)
            {
                CounterProvider defaultProvider = null;
                _console.Out.WriteLine($"--counters is unspecified. Monitoring System.Runtime counters by default.");

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
            string providerString = BuildProviderString();
            if (providerString.Length == 0)
            {
                return ReturnCode.ArgumentError;
            }

            _renderer.Initialize();

            Task monitorTask = new Task(() => {
                try
                {
                    _session = _diagnosticsClient.StartEventPipeSession(Trace.Extensions.ToProviders(providerString), false, 10);
                    if (_resumeRuntime)
                    {
                        _diagnosticsClient.ResumeRuntime();
                    }
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
                        return ReturnCode.Ok;
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
            return await Task.FromResult(ReturnCode.Ok);
        }
    }
}
