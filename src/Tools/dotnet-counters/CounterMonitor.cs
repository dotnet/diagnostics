// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Rendering;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal class CounterMonitor : ICountersLogger
    {
        private const int BufferDelaySecs = 1;
        private const string EventCountersProviderPrefix = "EventCounters\\";
        private int _processId;
        private TextWriter _stdOutput;
        private TextWriter _stdError;
        private List<EventPipeCounterGroup> _counterList;
        private ICounterRenderer _renderer;
        private string _output;
        private bool _pauseCmdSet;
        private readonly TaskCompletionSource<ReturnCode> _shouldExit;
        private DiagnosticsClient _diagnosticsClient;
        private MetricsPipelineSettings _settings;

        private class ProviderEventState
        {
            public DateTime FirstReceiveTimestamp;
            public bool InstrumentEventObserved;
        }
        private readonly Dictionary<string, ProviderEventState> _providerEventStates = new();
        private readonly Queue<CounterPayload> _bufferedEvents = new();

        public CounterMonitor(TextWriter stdOutput, TextWriter stdError)
        {
            _pauseCmdSet = false;
            _shouldExit = new TaskCompletionSource<ReturnCode>();
            _stdOutput = stdOutput;
            _stdError = stdError;
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
            // if EventCounters were explicitly requested on the command-line then show them always
            if (_counterList.Where(group => group.ProviderName == payload.CounterMetadata.ProviderName && group.Type == CounterGroupType.EventCounter).Any())
            {
                CounterPayloadReceived((CounterPayload)payload);
                return;
            }

            // init providerEventState if this is the first time we've seen an event from this provider
            if (!_providerEventStates.TryGetValue(payload.CounterMetadata.ProviderName, out ProviderEventState providerState))
            {
                providerState = new ProviderEventState()
                {
                    FirstReceiveTimestamp = payload.Timestamp
                };
                _providerEventStates.Add(payload.CounterMetadata.ProviderName, providerState);
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
            if (providerState.FirstReceiveTimestamp + TimeSpan.FromSeconds(BufferDelaySecs) >= payload.Timestamp)
            {
                _bufferedEvents.Enqueue((CounterPayload)payload);
            }
            else
            {
                CounterPayloadReceived((CounterPayload)payload);
            }
        }

        private void CounterPayloadReceived(CounterPayload payload)
        {
            if (payload is AggregatePercentilePayload aggregatePayload)
            {
                foreach (Quantile quantile in aggregatePayload.Quantiles)
                {
                    (double key, double val) = quantile;
                    PercentilePayload percentilePayload = new(payload.CounterMetadata, payload.DisplayName, payload.DisplayUnits, AppendQuantile(payload.ValueTags, $"Percentile={key * 100}"), val, payload.Timestamp);
                    _renderer.CounterPayloadReceived(percentilePayload, _pauseCmdSet);
                }

            }
            else
            {
                _renderer.CounterPayloadReceived(payload, _pauseCmdSet);
            }
        }

        private static string AppendQuantile(string tags, string quantile) => string.IsNullOrEmpty(tags) ? quantile : $"{tags},{quantile}";

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
                    ProviderEventState providerEventState = _providerEventStates[payload.CounterMetadata.ProviderName];
                    if (providerEventState.InstrumentEventObserved)
                    {
                        _bufferedEvents.Dequeue();
                    }
                    else if (providerEventState.FirstReceiveTimestamp + TimeSpan.FromSeconds(BufferDelaySecs) < now)
                    {
                        _bufferedEvents.Dequeue();
                        CounterPayloadReceived((CounterPayload)payload);
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

        public async Task<ReturnCode> Monitor(
            CancellationToken ct,
            string counters,
            int processId,
            int refreshInterval,
            string name,
            string diagnosticPort,
            bool resumeRuntime,
            int maxHistograms,
            int maxTimeSeries,
            TimeSpan duration,
            bool showDeltas,
            string dsrouter)
        {
            try
            {
                // System.CommandLine does have an option to specify arguments as uint and it would validate they are non-negative. However the error
                // message is "Cannot parse argument '-1' for option '--maxTimeSeries' as expected type System.UInt32" which is not as user friendly.
                // If there was another option to leverage System.CommandLine that provides a little more user friendly error message we could switch
                // to it.
                ValidateNonNegative(maxHistograms, nameof(maxHistograms));
                ValidateNonNegative(maxTimeSeries, nameof(maxTimeSeries));
                if (!ProcessLauncher.Launcher.HasChildProc && !CommandUtils.ResolveProcessForAttach(processId, name, diagnosticPort, dsrouter, out _processId))
                {
                    return ReturnCode.ArgumentError;
                }
                ct.Register(() => _shouldExit.TrySetResult((int)ReturnCode.Ok));

                DiagnosticsClientBuilder builder = new("dotnet-counters", 10);
                using (DiagnosticsClientHolder holder = await builder.Build(ct, _processId, diagnosticPort, showChildIO: false, printLaunchCommand: false).ConfigureAwait(false))
                using (VirtualTerminalMode vTerm = VirtualTerminalMode.TryEnable())
                {
                    bool useAnsi = vTerm.IsEnabled;
                    if (holder == null)
                    {
                        return ReturnCode.Ok;
                    }
                    try
                    {
                        // the launch command may misinterpret app arguments as the old space separated
                        // provider list so we need to ignore it in that case
                        _counterList = ConfigureCounters(counters);
                        _renderer = new ConsoleWriter(new DefaultConsole(useAnsi), showDeltaColumn:showDeltas);
                        _diagnosticsClient = holder.Client;
                        _settings = new MetricsPipelineSettings();
                        _settings.Duration = duration == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : duration;
                        _settings.MaxHistograms = maxHistograms;
                        _settings.MaxTimeSeries = maxTimeSeries;
                        _settings.CounterIntervalSeconds = refreshInterval;
                        _settings.ResumeRuntime = resumeRuntime;
                        _settings.CounterGroups = _counterList.ToArray();
                        _settings.UseCounterRateAndValuePayloads = true;

                        bool useSharedSession = false;
                        if (_diagnosticsClient.GetProcessInfo().TryGetProcessClrVersion(out Version version))
                        {
                            useSharedSession = version.Major >= 8 ? true : false;
                        }
                        _settings.UseSharedSession = useSharedSession;

                        ReturnCode ret;
                        MetricsPipeline eventCounterPipeline = new(holder.Client, _settings, new[] { this });
                        await using (eventCounterPipeline.ConfigureAwait(false))
                        {
                            ret = await Start(eventCounterPipeline, ct).ConfigureAwait(false);
                        }
                        ProcessLauncher.Launcher.Cleanup();
                        return ret;
                    }
                    catch (OperationCanceledException)
                    {
                        //Cancellation token should automatically stop the session

                        _stdOutput.WriteLine($"Complete");
                        return ReturnCode.Ok;
                    }
                }
            }
            catch (CommandLineErrorException e)
            {
                _stdError.WriteLine(e.Message);
                return ReturnCode.ArgumentError;
            }
        }
        public async Task<ReturnCode> Collect(
            CancellationToken ct,
            string counters,
            int processId,
            int refreshInterval,
            CountersExportFormat format,
            string output,
            string name,
            string diagnosticPort,
            bool resumeRuntime,
            int maxHistograms,
            int maxTimeSeries,
            TimeSpan duration,
            string dsrouter)
        {
            try
            {
                // System.CommandLine does have an option to specify arguments as uint and it would validate they are non-negative. However the error
                // message is "Cannot parse argument '-1' for option '--maxTimeSeries' as expected type System.UInt32" which is not as user friendly.
                // If there was another option to leverage System.CommandLine that provides a little more user friendly error message we could switch
                // to it.
                ValidateNonNegative(maxHistograms, nameof(maxHistograms));
                ValidateNonNegative(maxTimeSeries, nameof(maxTimeSeries));
                if (!ProcessLauncher.Launcher.HasChildProc && !CommandUtils.ResolveProcessForAttach(processId, name, diagnosticPort, dsrouter, out _processId))
                {
                    return ReturnCode.ArgumentError;
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
                        // the launch command may misinterpret app arguments as the old space separated
                        // provider list so we need to ignore it in that case
                        _counterList = ConfigureCounters(counters);
                        _settings = new MetricsPipelineSettings();
                        _settings.Duration = duration == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : duration;
                        _settings.MaxHistograms = maxHistograms;
                        _settings.MaxTimeSeries = maxTimeSeries;
                        _settings.CounterIntervalSeconds = refreshInterval;
                        _settings.ResumeRuntime = resumeRuntime;
                        _settings.CounterGroups = _counterList.ToArray();
                        _output = output;
                        _diagnosticsClient = holder.Client;
                        if (_output.Length == 0)
                        {
                            _stdError.WriteLine("Output cannot be an empty string");
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
                            _stdError.WriteLine($"The output format {format} is not a valid output format.");
                            return ReturnCode.ArgumentError;
                        }

                        ReturnCode ret;
                        MetricsPipeline eventCounterPipeline = new(holder.Client, _settings, new[] { this });
                        await using (eventCounterPipeline.ConfigureAwait(false))
                        {
                            ret = await Start(pipeline: eventCounterPipeline, ct).ConfigureAwait(false);
                        }

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
                _stdError.WriteLine(e.Message);
                return ReturnCode.ArgumentError;
            }
        }

        private static void ValidateNonNegative(int value, string argName)
        {
            if (value < 0)
            {
                throw new CommandLineErrorException($"Argument --{argName} must be non-negative");
            }
        }

        internal List<EventPipeCounterGroup> ConfigureCounters(string commaSeparatedProviderListText)
        {
            List<EventPipeCounterGroup> counters = new();
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

            if (counters.Count == 0)
            {
                _stdOutput.WriteLine($"--counters is unspecified. Monitoring System.Runtime counters by default.");
                ParseCounterProvider("System.Runtime", counters);
            }
            return counters;
        }

        // parses a comma separated list of providers
        internal static List<EventPipeCounterGroup> ParseProviderList(string providerListText)
        {
            List<EventPipeCounterGroup> set = new();
            ParseProviderList(providerListText, set);
            return set;
        }

        // parses a comma separated list of providers
        internal static void ParseProviderList(string providerListText, List<EventPipeCounterGroup> counters)
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
        private static void ParseCounterProvider(string providerText, List<EventPipeCounterGroup> counters)
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
            CounterGroupType groupType = CounterGroupType.All;
            // EventCounters\ is a special prefix for a provider that marks it as an EventCounter provider.
            if (providerName.StartsWith(EventCountersProviderPrefix))
            {
                providerName = providerName.Substring(EventCountersProviderPrefix.Length);
                groupType = CounterGroupType.EventCounter;
            }

            // An empty set of counters means all counters are enabled.
            string[] enabledCounters = Array.Empty<string>();
            EventPipeCounterGroup preExistingGroup = counters.FirstOrDefault(group => group.ProviderName == providerName);
            if (preExistingGroup != null && preExistingGroup.Type != groupType)
            {
                throw new FormatException("Using the same provider name with and without the EventCounters\\ prefix in the counter list is not supported.");
            }
            if (tokens.Length == 2)
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
                enabledCounters = counterNames.Substring(0, counterNames.Length - 1).Split(',', StringSplitOptions.RemoveEmptyEntries);
            }

            // haven't seen this provider yet so add it
            if (preExistingGroup == null)
            {
                counters.Add(new EventPipeCounterGroup
                {
                    ProviderName = providerName,
                    CounterNames = enabledCounters,
                    Type = groupType
                });
            }
            // we've already seen the provider so merge the configurations
            else
            {
                // If the previous config had some specific counters and the new one also has specific counters then merge them.
                // Otherwise one of the two requested all counters so the union is also all counters.
                if (preExistingGroup.CounterNames.Length == 0 || enabledCounters.Length == 0)
                {
                    preExistingGroup.CounterNames = Array.Empty<string>();
                }
                else
                {
                    preExistingGroup.CounterNames = preExistingGroup.CounterNames.Union(enabledCounters).ToArray();
                }
            }
        }

        private async Task<ReturnCode> Start(MetricsPipeline pipeline, CancellationToken token)
        {
            _renderer.Initialize();
            Task monitorTask = new(async () => {
                try
                {
                    Task runAsyncTask = await pipeline.StartAsync(token).ConfigureAwait(false);
                    await runAsyncTask.ConfigureAwait(false);
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

            while (!_shouldExit.Task.Wait(250, token))
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
                await pipeline.StopAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (PipelineException)
            {
            }

            return await _shouldExit.Task.ConfigureAwait(false);
        }

        void ICountersLogger.Log(ICounterPayload payload)
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
                if (payload is ErrorPayload errorPayload)
                {
                    // Several of the error messages used by Dotnet are specific to the tool;
                    // the error messages found in errorPayload.ErrorMessage are not tool-specific.
                    // This replaces the generic error messages with specific ones as-needed.
                    string errorMessage = string.Empty;
                    switch (errorPayload.EventType)
                    {
                        case EventType.HistogramLimitError:
                            errorMessage = $"Warning: Histogram tracking limit ({_settings.MaxHistograms}) reached. Not all data is being shown." + Environment.NewLine +
                "The limit can be changed with --maxHistograms but will use more memory in the target process.";
                            break;
                        case EventType.TimeSeriesLimitError:
                            errorMessage = $"Warning: Time series tracking limit ({_settings.MaxTimeSeries}) reached. Not all data is being shown." + Environment.NewLine +
                "The limit can be changed with --maxTimeSeries but will use more memory in the target process.";
                            break;
                        case EventType.ErrorTargetProcess:
                        case EventType.MultipleSessionsNotSupportedError:
                        case EventType.MultipleSessionsConfiguredIncorrectlyError:
                        case EventType.ObservableInstrumentCallbackError:
                        default:
                            errorMessage = errorPayload.ErrorMessage;
                            break;
                    }

                    _renderer.SetErrorText(errorMessage);

                    if (errorPayload.EventType.IsSessionStartupError())
                    {
                        _shouldExit.TrySetResult(ReturnCode.SessionCreationError);
                    }
                    else if (errorPayload.EventType.IsTracingError())
                    {
                        _shouldExit.TrySetResult(ReturnCode.TracingError);
                    }
                    else if (errorPayload.EventType.IsNonFatalError())
                    {
                        // Don't need to exit for NonFatalError
                    }
                    else
                    {
                        _shouldExit.TrySetResult(ReturnCode.UnknownError);
                    }
                }
                else if (payload is CounterEndedPayload counterEnded)
                {
                    _renderer.CounterStopped(counterEnded);
                }
                else if (payload.IsMeter)
                {
                    MeterInstrumentEventObserved(payload.CounterMetadata.ProviderName, payload.Timestamp);
                    if (payload.EventType.IsValuePublishedEvent())
                    {
                        CounterPayloadReceived((CounterPayload)payload);
                    }
                }
                else
                {
                    HandleDiagnosticCounter(payload);
                }
            }
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
