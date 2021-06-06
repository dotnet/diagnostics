// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Internal.Common.Utils;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private int _processId;
        private int _interval;
        private CounterSet _counterList;
        private CancellationToken _ct;
        private IConsole _console;
        private ICounterRenderer _renderer;
        private string _output;
        private bool pauseCmdSet;
        private ManualResetEvent shouldExit;
        private bool _resumeRuntime;
        private DiagnosticsClient _diagnosticsClient;
        private EventPipeSession _session;

        public CounterMonitor()
        {
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
                if (!_counterList.Contains(obj.ProviderName, payloadFields["Name"].ToString())) return;

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
            try
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
                        _console = console;
                        // the launch command may misinterpret app arguments as the old space separated
                        // provider list so we need to ignore it in that case
                        _counterList = ConfigureCounters(counters, _processId != 0 ? counter_list : null);
                        _ct = ct;
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
            catch(CommandLineErrorException e)
            {
                console.Error.WriteLine(e.Message);
                return ReturnCode.ArgumentError;
            }
        }


        public async Task<int> Collect(CancellationToken ct, List<string> counter_list, string counters, IConsole console, int processId, int refreshInterval, CountersExportFormat format, string output, string name, string diagnosticPort, bool resumeRuntime)
        {
            try
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
                        _console = console;
                        // the launch command may misinterpret app arguments as the old space separated
                        // provider list so we need to ignore it in that case
                        _counterList = ConfigureCounters(counters, _processId != 0 ? counter_list : null);
                        _ct = ct;
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
                        _resumeRuntime = resumeRuntime;
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
            catch(CommandLineErrorException e)
            {
                console.Error.WriteLine(e.Message);
                return ReturnCode.ArgumentError;
            }
        }

        internal CounterSet ConfigureCounters(string commaSeparatedProviderListText, List<string> providerList)
        {
            CounterSet counters = new CounterSet();
            try
            {
                if(commaSeparatedProviderListText != null)
                {
                    ParseProviderList(commaSeparatedProviderListText, counters);
                }
            }
            catch(FormatException e)
            {
                // the FormatException message strings thrown by ParseProviderList are controlled
                // by us and anticipate being integrated into the command-line error text.
                throw new CommandLineErrorException("Error parsing --counters argument: " + e.Message);
            }

            if (providerList != null)
            {
                try
                {
                    foreach (string providerText in providerList)
                    {
                        ParseCounterProvider(providerText, counters);
                    }
                }
                catch (FormatException e)
                {
                    // the FormatException message strings thrown by ParseCounterProvider are controlled
                    // by us and anticipate being integrated into the command-line error text.
                    throw new CommandLineErrorException("Error parsing counter_list: " + e.Message);
                }
            }

            if (counters.IsEmpty)
            {
                _console.Out.WriteLine($"--counters is unspecified. Monitoring System.Runtime counters by default.");
                counters.AddAllProviderCounters("System.Runtime");
            }
            return counters;
        }

        // parses a comma separated list of providers
        internal CounterSet ParseProviderList(string providerListText)
        {
            CounterSet set = new CounterSet();
            ParseProviderList(providerListText, set);
            return set;
        }

        // parses a comma separated list of providers
        internal void ParseProviderList(string providerListText, CounterSet counters)
        {
            bool inParen = false;
            int startIdx = -1;
            int i = 0;
            for (; i < providerListText.Length; i++)
            {
                if (!inParen)
                {
                    if (providerListText[i] == '[')
                    {
                        inParen = true;
                        continue;
                    }
                    else if (providerListText[i] == ',')
                    {
                        if (startIdx < 0)
                        {
                            throw new FormatException("Expected non-empty counter_provider");
                        }
                        ParseCounterProvider(providerListText.Substring(startIdx, i - startIdx), counters);
                        startIdx = -1;
                    }
                    else if (startIdx == -1 && providerListText[i] != ' ')
                    {
                        startIdx = i;
                    }
                }
                else if (inParen && providerListText[i] == ']')
                {
                    inParen = false;
                }
            }
            if(inParen)
            {
                throw new FormatException("Expected to find closing ']' in counter_provider");
            }
            if (startIdx < 0)
            {
                throw new FormatException("Expected non-empty counter_provider");
            }
            ParseCounterProvider(providerListText.Substring(startIdx, i - startIdx), counters);
        }

        // Parses a string in the format:
        // provider := <provider_name><optional_counter_list>
        // provider_name := string not containing '['
        // optional_counter_list := [<comma_separated_counter_names>]
        // For example:
        //   System.Runtime
        //   System.Runtime[exception-count]
        //   System.Runtime[exception-count,cpu-usage]
        void ParseCounterProvider(string providerText, CounterSet counters)
        {
            string[] tokens = providerText.Split('[');
            if(tokens.Length == 0)
            {
                throw new FormatException("Expected non-empty counter_provider");
            }
            if(tokens.Length > 2)
            {
                throw new FormatException("Expected at most one '[' in counter_provider");
            }
            string providerName = tokens[0];
            if (tokens.Length == 1)
            {
                counters.AddAllProviderCounters(providerName); // Only a provider name was specified
            }
            else
            {
                string counterNames = tokens[1];
                if(!counterNames.EndsWith(']'))
                {
                    if(counterNames.IndexOf(']') == -1)
                    {
                        throw new FormatException("Expected to find closing ']' in counter_provider");
                    }
                    else
                    {
                        throw new FormatException("Unexpected characters after closing ']' in counter_provider");
                    }
                }
                string[] enabledCounters = counterNames.Substring(0, counterNames.Length - 1).Split(',', StringSplitOptions.RemoveEmptyEntries);
                counters.AddProviderCounters(providerName, enabledCounters);
            }
        }

        private EventPipeProvider[] GetEventPipeProviders()
        {
            return _counterList.Providers.Select(providerName => new EventPipeProvider(providerName, EventLevel.Error, 0, new Dictionary<string, string>()
                {{ "EventCounterIntervalSec", _interval.ToString() }})).ToArray();
        }

        private async Task<int> Start()
        {
            EventPipeProvider[] providers = GetEventPipeProviders();
            _renderer.Initialize();

            Task monitorTask = new Task(() => {
                try
                {
                    _session = _diagnosticsClient.StartEventPipeSession(providers, false, 10);
                    if (_resumeRuntime)
                    {
                        try
                        {
                            _diagnosticsClient.ResumeRuntime();
                        }
                        catch (UnsupportedCommandException)
                        {
                            // Noop if the command is unknown since the target process is most likely a 3.1 app.
                        }
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
