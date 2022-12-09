// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Internal.Common.Utils;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.Monitoring;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal class CounterMonitor : ICountersLogger
    {
        private const int BufferDelaySecs = 1;

        private int _processId;
        private CounterSet _counterList;
        private CancellationToken _ct;
        private IConsole _console;
        private ICounterRenderer _renderer;
        private string _output;
        private bool _pauseCmdSet;
        private readonly TaskCompletionSource<int> _shouldExit;
        private DiagnosticsClient _diagnosticsClient;
        private string _metricsEventSourceSessionId;
        private EventPipeCounterPipelineSettings _settings;

        private class ProviderEventState
        {
            public DateTime FirstReceiveTimestamp;
            public bool InstrumentEventObserved;
        }
        private readonly Dictionary<string, ProviderEventState> _providerEventStates = new();
        private readonly Queue<CounterPayload> _bufferedEvents = new();

        public CounterMonitor()
        {
            _pauseCmdSet = false;
            _metricsEventSourceSessionId = Guid.NewGuid().ToString();
            _shouldExit = new TaskCompletionSource<int>();
        }

        private void DynamicAllMonitor(ICounterPayload obj)
        {
            if (_shouldExit.Task.IsCompleted)
            {
                return;
            }

            lock (this)
            {
                // If we are paused, ignore the event.
                // There's a potential race here between the two tasks but not a huge deal if we miss by one event.
                _renderer.ToggleStatus(_pauseCmdSet);
                if (obj is ErrorPayload errorPayload)
                {
                    _renderer.SetErrorText(errorPayload.ErrorMessage);
                    switch(errorPayload.ErrorType)
                    {
                        case ErrorType.SessionStartupError:
                            _shouldExit.TrySetResult(ReturnCode.SessionCreationError);
                            break;
                        case ErrorType.TracingError:
                            _shouldExit.TrySetResult(ReturnCode.TracingError);
                            break;
                    }
                }
                else if (obj is CounterEndedPayload counterEnded)
                {
                    _renderer.CounterStopped(counterEnded);
                }
                else if (obj.IsMeter)
                {
                    MeterInstrumentEventObserved(obj.Provider, obj.Name, obj.Timestamp);
                    if (obj is not InstrumentationStartedPayload)
                    {
                        _renderer.CounterPayloadReceived((CounterPayload)obj, _pauseCmdSet);
                    }
                }
                else
                {
                    HandleDiagnosticCounter(obj);
                }
            }
        }

        private void MeterInstrumentEventObserved(string meterName, DateTime timestamp)
        {
            if (!_providerEventStates.TryGetValue(meterName, out ProviderEventState providerEventState))
            {
                providerEventState = new ProviderEventState()
                {
                    FirstReceiveTimestamp = timestamp,
                    InstrumentEventObserved = true
                };
                _providerEventStates.Add(meterName, providerEventState);
            }
            else
            {
                providerEventState.InstrumentEventObserved = true;
            }
        }

        private void HandleDiagnosticCounter(ICounterPayload payload)
        {
            // init providerEventState if this is the first time we've seen an event from this provider
            if (!_providerEventStates.TryGetValue(payload.Provider, out ProviderEventState providerState))
            {
                providerState = new ProviderEventState()
                {
                    FirstReceiveTimestamp = payload.Timestamp
                };
                _providerEventStates.Add(payload.Provider, providerState);
            }

            // we give precedence to instrument events over diagnostic counter events. If we are seeing
            // both then drop this one.
            if (providerState.InstrumentEventObserved)
            {
                return;
            }

            // If we saw the first event for this provider recently then a duplicate instrument event may still be
            // coming. We'll buffer this event for a while and then render it if it remains unduplicated for
            // a while.
            // This is all best effort, if we do show the DiagnosticCounter event and then an instrument event shows up
            // later the renderer may observe some odd behavior like changes in the counter metadata, oddly timed reporting
            // intervals, or counters that stop reporting.
            // I'm gambling this is good enough that the behavior will never be seen in practice, but if it is we could
            // either adjust the time delay or try to improve how the renderers handle it.
            if(providerState.FirstReceiveTimestamp + TimeSpan.FromSeconds(BufferDelaySecs) >= payload.Timestamp)
            {
                _bufferedEvents.Enqueue((CounterPayload)payload);
            }
            else
            {
                _renderer.CounterPayloadReceived((CounterPayload)payload, _pauseCmdSet);
            }
        }

        // when receiving DiagnosticCounter events we may have buffered them to wait for
        // duplicate instrument events. If we've waited long enough then we should remove
        // them from the buffer and render them.
        private void HandleBufferedEvents()
        {
            DateTime now = DateTime.Now;
            lock (this)
            {
                while (_bufferedEvents.Count != 0)
                {
                    CounterPayload payload = _bufferedEvents.Peek();
                    ProviderEventState providerEventState = _providerEventStates[payload.Provider];
                    if (providerEventState.InstrumentEventObserved)
                    {
                        _bufferedEvents.Dequeue();
                    }
                    else if (providerEventState.FirstReceiveTimestamp + TimeSpan.FromSeconds(BufferDelaySecs) < now)
                    {
                        _bufferedEvents.Dequeue();
                        _renderer.CounterPayloadReceived(payload, _pauseCmdSet);
                    }
                    else
                    {
                        // technically an event that is eligible to be unbuffered earlier could be waiting behind a
                        // buffered event that will wait longer, but we don't expect this variation to matter for
                        // our scenarios. At worst an event might wait up to 2*BufferDelaySecs. If there is a scenario
                        // where it matters we could scan the entire queue rather than just the front of it.
                        break;
                    }
                }
            }
        }

        public async Task<int> Monitor(
            CancellationToken ct,
            List<string> counter_list,
            string counters,
            IConsole console,
            int processId,
            int refreshInterval,
            string name,
            string diagnosticPort,
            bool resumeRuntime,
            int maxHistograms,
            int maxTimeSeries,
            TimeSpan duration)
        {
            try
            {
                // System.CommandLine does have an option to specify arguments as uint and it would validate they are non-negative. However the error
                // message is "Cannot parse argument '-1' for option '--maxTimeSeries' as expected type System.UInt32" which is not as user friendly.
                // If there was another option to leverage System.CommandLine that provides a little more user friendly error message we could switch
                // to it.
                ValidateNonNegative(maxHistograms, nameof(maxHistograms));
                ValidateNonNegative(maxTimeSeries, nameof(maxTimeSeries));
                if (!ProcessLauncher.Launcher.HasChildProc && !CommandUtils.ValidateArgumentsForAttach(processId, name, diagnosticPort, out _processId))
                {
                    return (int)ReturnCode.ArgumentError;
                }
                ct.Register(() => _shouldExit.TrySetResult((int)ReturnCode.Ok));

                DiagnosticsClientBuilder builder = new("dotnet-counters", 10);
                using (DiagnosticsClientHolder holder = await builder.Build(ct, _processId, diagnosticPort, showChildIO: false, printLaunchCommand: false).ConfigureAwait(false))
                using (VirtualTerminalMode vTerm = VirtualTerminalMode.TryEnable())
                {
                    bool useAnsi = vTerm.IsEnabled;
                    if (holder == null)
                    {
                        return (int)ReturnCode.Ok;
                    }
                    try
                    {
                        _console = console;
                        // the launch command may misinterpret app arguments as the old space separated
                        // provider list so we need to ignore it in that case
                        _counterList = ConfigureCounters(counters, _processId != 0 ? counter_list : null);
                        _ct = ct;
                        _renderer = new ConsoleWriter(useAnsi);
                        _diagnosticsClient = holder.Client;
                        _settings = new EventPipeCounterPipelineSettings();
                        _settings.Duration = duration == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : duration;
                        _settings.MaxHistograms = maxHistograms;
                        _settings.MaxTimeSeries = maxTimeSeries;
                        _settings.CounterIntervalSeconds = refreshInterval;
                        _settings.ResumeRuntime = resumeRuntime;
                        _settings.CounterGroups = GetEventPipeProviders();

                        await using EventCounterPipeline eventCounterPipeline = new EventCounterPipeline(holder.Client, _settings, new[] { this });
                        int ret = await Start(eventCounterPipeline, ct);
                        ProcessLauncher.Launcher.Cleanup();
                        return ret;
                    }
                    catch (OperationCanceledException)
                    {
                        //Cancellation token should automatically stop the session

                        console.Out.WriteLine($"Complete");
                        return (int)ReturnCode.Ok;
                    }
                }
            }
            catch (CommandLineErrorException e)
            {
                console.Error.WriteLine(e.Message);
                return (int)ReturnCode.ArgumentError;
            }
        }
        public async Task<int> Collect(
            CancellationToken ct,
            List<string> counter_list,
            string counters,
            IConsole console,
            int processId,
            int refreshInterval,
            CountersExportFormat format,
            string output,
            string name,
            string diagnosticPort,
            bool resumeRuntime,
            int maxHistograms,
            int maxTimeSeries,
            TimeSpan duration)
        {
            try
            {
                // System.CommandLine does have an option to specify arguments as uint and it would validate they are non-negative. However the error
                // message is "Cannot parse argument '-1' for option '--maxTimeSeries' as expected type System.UInt32" which is not as user friendly.
                // If there was another option to leverage System.CommandLine that provides a little more user friendly error message we could switch
                // to it.
                ValidateNonNegative(maxHistograms, nameof(maxHistograms));
                ValidateNonNegative(maxTimeSeries, nameof(maxTimeSeries));
                if (!ProcessLauncher.Launcher.HasChildProc && !CommandUtils.ValidateArgumentsForAttach(processId, name, diagnosticPort, out _processId))
                {
                    return (int)ReturnCode.ArgumentError;
                }

                ct.Register(() => _shouldExit.TrySetResult((int)ReturnCode.Ok));

                DiagnosticsClientBuilder builder = new("dotnet-counters", 10);
                using (DiagnosticsClientHolder holder = await builder.Build(ct, _processId, diagnosticPort, showChildIO: false, printLaunchCommand: false).ConfigureAwait(false))
                {
                    if (holder == null)
                    {
                        return (int)ReturnCode.Ok;
                    }

                    try
                    {
                        _console = console;
                        // the launch command may misinterpret app arguments as the old space separated
                        // provider list so we need to ignore it in that case
                        _counterList = ConfigureCounters(counters, _processId != 0 ? counter_list : null);
                        _ct = ct;
                        _settings = new EventPipeCounterPipelineSettings();
                        _settings.Duration = duration == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : duration;
                        _settings.MaxHistograms = maxHistograms;
                        _settings.MaxTimeSeries = maxTimeSeries;
                        _settings.CounterIntervalSeconds = refreshInterval;
                        _settings.ResumeRuntime = resumeRuntime;
                        _settings.CounterGroups = GetEventPipeProviders();
                        _output = output;
                        _diagnosticsClient = holder.Client;
                        if (_output.Length == 0)
                        {
                            _console.Error.WriteLine("Output cannot be an empty string");
                            return (int)ReturnCode.ArgumentError;
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
                            return (int)ReturnCode.ArgumentError;
                        }
                        await using EventCounterPipeline eventCounterPipeline = new EventCounterPipeline(holder.Client, _settings, new[] { this });

                        int ret = await Start(pipeline: eventCounterPipeline, ct);
                        return ret;
                    }
                    catch (OperationCanceledException)
                    {
                        //Cancellation token should automatically stop the session
                        return ReturnCode.Ok;
                    }
                }
            }
            catch (CommandLineErrorException e)
            {
                console.Error.WriteLine(e.Message);
                return (int)ReturnCode.ArgumentError;
            }
        }

        private static void ValidateNonNegative(int value, string argName)
        {
            if (value < 0)
            {
                throw new CommandLineErrorException($"Argument --{argName} must be non-negative");
            }
        }

        internal CounterSet ConfigureCounters(string commaSeparatedProviderListText, List<string> providerList)
        {
            CounterSet counters = new();
            try
            {
                if (commaSeparatedProviderListText != null)
                {
                    ParseProviderList(commaSeparatedProviderListText, counters);
                }
            }
            catch (FormatException e)
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
        internal static CounterSet ParseProviderList(string providerListText)
        {
            CounterSet set = new();
            ParseProviderList(providerListText, set);
            return set;
        }

        // parses a comma separated list of providers
        internal static void ParseProviderList(string providerListText, CounterSet counters)
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
            if (inParen)
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
        private static void ParseCounterProvider(string providerText, CounterSet counters)
        {
            string[] tokens = providerText.Split('[');
            if (tokens.Length == 0)
            {
                throw new FormatException("Expected non-empty counter_provider");
            }
            if (tokens.Length > 2)
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
                if (!counterNames.EndsWith(']'))
                {
                    if (!counterNames.Contains(']'))
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

        private EventPipeCounterGroup[] GetEventPipeProviders() =>
            _counterList.Providers.Select(provider => new EventPipeCounterGroup
            {
                ProviderName = provider,
                CounterNames = _counterList.GetCounters(provider).ToArray()
            }).ToArray();

        private async Task<int> Start(EventCounterPipeline pipeline, CancellationToken token)
        {
            _renderer.Initialize();
            Task monitorTask = new Task(async () => {
                try
                {
                    await await pipeline.StartAsync(token);
                }
                catch (DiagnosticsClientException ex)
                {
                    Console.WriteLine($"Failed to start the counter session: {ex}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] {ex}");
                }
                finally
                {
                    _shouldExit.TrySetResult((int)ReturnCode.Ok);
                }
            });

            monitorTask.Start();
 
            while(!_shouldExit.Task.Wait(250))
            {
                HandleBufferedEvents();
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    ConsoleKey cmd = Console.ReadKey(true).Key;
                    if (cmd == ConsoleKey.Q)
                    {
                        break;
                    }
                    else if (cmd == ConsoleKey.P)
                    {
                        _pauseCmdSet = true;
                    }
                    else if (cmd == ConsoleKey.R)
                    {
                        _pauseCmdSet = false;
                    }
                }
            }

            try
            {
                await pipeline.StopAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (PipelineException)
            {
            }
            
            return await _shouldExit.Task;
        }

        public void Log(ICounterPayload counter)
        {
            DynamicAllMonitor(counter);
        }

        public Task PipelineStarted(CancellationToken token)
        {
            _renderer.EventPipeSourceConnected();
            return Task.CompletedTask;
        }

        public Task PipelineStopped(CancellationToken token)
        {
            _renderer.Stop();
            return Task.CompletedTask;
        }
    }
}
