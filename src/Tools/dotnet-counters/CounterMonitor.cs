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

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterMonitor
    {
        private const int BufferDelaySecs = 1;

        private int _processId;
        private int _interval;
        private CounterSet _counterList;
        private CancellationToken _ct;
        private IConsole _console;
        private ICounterRenderer _renderer;
        private string _output;
        private bool _pauseCmdSet;
        private readonly TaskCompletionSource<int> _shouldExit;
        private bool _resumeRuntime;
        private DiagnosticsClient _diagnosticsClient;
        private EventPipeSession _session;
        private readonly string _metricsEventSourceSessionId;
        private int _maxTimeSeries;
        private int _maxHistograms;
        private TimeSpan _duration;

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

        private void DynamicAllMonitor(TraceEvent obj)
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

                if (obj.ProviderName == "System.Diagnostics.Metrics")
                {
                    if (obj.EventName == "UpDownCounterRateValuePublished")
                    {
                        HandleCounterRate(obj);
                    }
                    if (obj.EventName == "BeginInstrumentReporting")
                    {
                        HandleBeginInstrumentReporting(obj);
                    }
                    if (obj.EventName == "HistogramValuePublished")
                    {
                        HandleHistogram(obj);
                    }
                    else if (obj.EventName == "GaugeValuePublished")
                    {
                        HandleGauge(obj);
                    }
                    else if (obj.EventName == "CounterRateValuePublished")
                    {
                        HandleCounterRate(obj);
                    }
                    else if (obj.EventName == "TimeSeriesLimitReached")
                    {
                        HandleTimeSeriesLimitReached(obj);
                    }
                    else if (obj.EventName == "HistogramLimitReached")
                    {
                        HandleHistogramLimitReached(obj);
                    }
                    else if (obj.EventName == "Error")
                    {
                        HandleError(obj);
                    }
                    else if (obj.EventName == "ObservableInstrumentCallbackError")
                    {
                        HandleObservableInstrumentCallbackError(obj);
                    }
                    else if (obj.EventName == "MultipleSessionsNotSupportedError")
                    {
                        HandleMultipleSessionsNotSupportedError(obj);
                    }
                }
                else if (obj.EventName == "EventCounters")
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

        private void HandleBeginInstrumentReporting(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            string meterName = (string)obj.PayloadValue(1);
            // string instrumentName = (string)obj.PayloadValue(3);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            MeterInstrumentEventObserved(meterName, obj.TimeStamp);
        }

        private void HandleCounterRate(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string rateText = (string)obj.PayloadValue(6);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            MeterInstrumentEventObserved(meterName, obj.TimeStamp);

            // the value might be an empty string indicating no measurement was provided this collection interval
            if (double.TryParse(rateText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            {
                CounterPayload payload = new RatePayload(meterName, instrumentName, null, unit, tags, rate, _interval, obj.TimeStamp);
                _renderer.CounterPayloadReceived(payload, _pauseCmdSet);
            }

        }

        private void HandleGauge(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string lastValueText = (string)obj.PayloadValue(6);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            MeterInstrumentEventObserved(meterName, obj.TimeStamp);

            // the value might be an empty string indicating no measurement was provided this collection interval
            if (double.TryParse(lastValueText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double lastValue))
            {
                CounterPayload payload = new GaugePayload(meterName, instrumentName, null, unit, tags, lastValue, obj.TimeStamp);
                _renderer.CounterPayloadReceived(payload, _pauseCmdSet);
            }
            else
            {
                // for observable instruments we assume the lack of data is meaningful and remove it from the UI
                CounterPayload payload = new RatePayload(meterName, instrumentName, null, unit, tags, 0, _interval, obj.TimeStamp);
                _renderer.CounterStopped(payload);
            }
        }

        private void HandleHistogram(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string quantilesText = (string)obj.PayloadValue(6);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            MeterInstrumentEventObserved(meterName, obj.TimeStamp);
            KeyValuePair<double, double>[] quantiles = ParseQuantiles(quantilesText);
            foreach ((double key, double val) in quantiles)
            {
                CounterPayload payload = new PercentilePayload(meterName, instrumentName, null, unit, AppendQuantile(tags, $"Percentile={key * 100}"), val, obj.TimeStamp);
                _renderer.CounterPayloadReceived(payload, _pauseCmdSet);
            }
        }

        private void HandleHistogramLimitReached(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            _renderer.SetErrorText(
                $"Warning: Histogram tracking limit ({_maxHistograms}) reached. Not all data is being shown." + Environment.NewLine +
                "The limit can be changed with --maxHistograms but will use more memory in the target process."
                );
        }

        private void HandleTimeSeriesLimitReached(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            _renderer.SetErrorText(
                $"Warning: Time series tracking limit ({_maxTimeSeries}) reached. Not all data is being shown." + Environment.NewLine +
                "The limit can be changed with --maxTimeSeries but will use more memory in the target process."
                );
        }

        private void HandleError(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            string error = (string)obj.PayloadValue(1);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            _renderer.SetErrorText(
                "Error reported from target process:" + Environment.NewLine +
                error
                );
            _shouldExit.TrySetResult((int)ReturnCode.TracingError);
        }

        private void HandleObservableInstrumentCallbackError(TraceEvent obj)
        {
            string sessionId = (string)obj.PayloadValue(0);
            string error = (string)obj.PayloadValue(1);
            if (sessionId != _metricsEventSourceSessionId)
            {
                return;
            }
            _renderer.SetErrorText(
                "Exception thrown from an observable instrument callback in the target process:" + Environment.NewLine +
                error
                );
        }

        private void HandleMultipleSessionsNotSupportedError(TraceEvent obj)
        {
            string runningSessionId = (string)obj.PayloadValue(0);
            if (runningSessionId == _metricsEventSourceSessionId)
            {
                // If our session is the one that is running then the error is not for us,
                // it is for some other session that came later
                return;
            }
            _renderer.SetErrorText(
                "Error: Another metrics collection session is already in progress for the target process, perhaps from another tool?" + Environment.NewLine +
                "Concurrent sessions are not supported.");
            _shouldExit.TrySetResult((int)ReturnCode.SessionCreationError);
        }

        private static KeyValuePair<double, double>[] ParseQuantiles(string quantileList)
        {
            string[] quantileParts = quantileList.Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<KeyValuePair<double, double>> quantiles = new();
            foreach (string quantile in quantileParts)
            {
                string[] keyValParts = quantile.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (keyValParts.Length != 2)
                {
                    continue;
                }
                if (!double.TryParse(keyValParts[0], NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double key))
                {
                    continue;
                }
                if (!double.TryParse(keyValParts[1], NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    continue;
                }
                quantiles.Add(new KeyValuePair<double, double>(key, val));
            }
            return quantiles.ToArray();
        }

        private static string AppendQuantile(string tags, string quantile) => string.IsNullOrEmpty(tags) ? quantile : $"{tags},{quantile}";

        private void HandleDiagnosticCounter(TraceEvent obj)
        {
            IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
            IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

            // If it's not a counter we asked for, ignore it.
            string name = payloadFields["Name"].ToString();
            if (!_counterList.Contains(obj.ProviderName, name))
            {
                return;
            }

            // init providerEventState if this is the first time we've seen an event from this provider
            if (!_providerEventStates.TryGetValue(obj.ProviderName, out ProviderEventState providerState))
            {
                providerState = new ProviderEventState()
                {
                    FirstReceiveTimestamp = obj.TimeStamp
                };
                _providerEventStates.Add(obj.ProviderName, providerState);
            }

            // we give precedence to instrument events over diagnostic counter events. If we are seeing
            // both then drop this one.
            if (providerState.InstrumentEventObserved)
            {
                return;
            }

            CounterPayload payload;
            if (payloadFields["CounterType"].Equals("Sum"))
            {
                payload = new RatePayload(
                    obj.ProviderName,
                    name,
                    payloadFields["DisplayName"].ToString(),
                    payloadFields["DisplayUnits"].ToString(),
                    null,
                    (double)payloadFields["Increment"],
                    _interval,
                    obj.TimeStamp);
            }
            else
            {
                payload = new GaugePayload(
                    obj.ProviderName,
                    name,
                    payloadFields["DisplayName"].ToString(),
                    payloadFields["DisplayUnits"].ToString(),
                    null,
                    (double)payloadFields["Mean"],
                    obj.TimeStamp);
            }

            // If we saw the first event for this provider recently then a duplicate instrument event may still be
            // coming. We'll buffer this event for a while and then render it if it remains unduplicated for
            // a while.
            // This is all best effort, if we do show the DiagnosticCounter event and then an instrument event shows up
            // later the renderer may obsserve some odd behavior like changes in the counter metadata, oddly timed reporting
            // intervals, or counters that stop reporting.
            // I'm gambling this is good enough that the behavior will never be seen in practice, but if it is we could
            // either adjust the time delay or try to improve how the renderers handle it.
            if (providerState.FirstReceiveTimestamp + TimeSpan.FromSeconds(BufferDelaySecs) >= obj.TimeStamp)
            {
                _bufferedEvents.Enqueue(payload);
            }
            else
            {
                _renderer.CounterPayloadReceived(payload, _pauseCmdSet);
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
                    ProviderEventState providerEventState = _providerEventStates[payload.ProviderName];
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

        private void StopMonitor()
        {
            try
            {
                _session?.Stop();
            }
            catch (EndOfStreamException ex)
            {
                // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                Debug.WriteLine($"[ERROR] {ex}");
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
                        _interval = refreshInterval;
                        _maxHistograms = maxHistograms;
                        _maxTimeSeries = maxTimeSeries;
                        _renderer = new ConsoleWriter(useAnsi);
                        _diagnosticsClient = holder.Client;
                        _resumeRuntime = resumeRuntime;
                        _duration = duration;
                        int ret = await Start().ConfigureAwait(false);
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
                        _interval = refreshInterval;
                        _maxHistograms = maxHistograms;
                        _maxTimeSeries = maxTimeSeries;
                        _output = output;
                        _diagnosticsClient = holder.Client;
                        _duration = duration;
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
                        _resumeRuntime = resumeRuntime;
                        int ret = await Start().ConfigureAwait(false);
                        return ret;
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            _session.Stop();
                        }
                        catch (Exception) { } // session.Stop() can throw if target application already stopped before we send the stop command.
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

        private EventPipeProvider[] GetEventPipeProviders()
        {
            // EventSources support EventCounter based metrics directly
            IEnumerable<EventPipeProvider> eventCounterProviders = _counterList.Providers.Select(
                providerName => new EventPipeProvider(providerName, EventLevel.Error, 0, new Dictionary<string, string>()
                {{ "EventCounterIntervalSec", _interval.ToString() }}));

            //System.Diagnostics.Metrics EventSource supports the new Meter/Instrument APIs
            const long TimeSeriesValues = 0x2;
            StringBuilder metrics = new();
            foreach (string provider in _counterList.Providers)
            {
                if (metrics.Length != 0)
                {
                    metrics.Append(',');
                }
                if (_counterList.IncludesAllCounters(provider))
                {
                    metrics.Append(provider);
                }
                else
                {
                    string[] providerCounters = _counterList.GetCounters(provider).Select(counter => $"{provider}\\{counter}").ToArray();
                    metrics.Append(string.Join(',', providerCounters));
                }
            }
            EventPipeProvider metricsEventSourceProvider =
                new("System.Diagnostics.Metrics", EventLevel.Informational, TimeSeriesValues,
                    new Dictionary<string, string>()
                    {
                        { "SessionId", _metricsEventSourceSessionId },
                        { "Metrics", metrics.ToString() },
                        { "RefreshInterval", _interval.ToString() },
                        { "MaxTimeSeries", _maxTimeSeries.ToString() },
                        { "MaxHistograms", _maxHistograms.ToString() }
                    }
                );

            return eventCounterProviders.Append(metricsEventSourceProvider).ToArray();
        }

        private Task<int> Start()
        {
            EventPipeProvider[] providers = GetEventPipeProviders();
            _renderer.Initialize();

            Task monitorTask = new(() => {
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
                    EventPipeEventSource source = new(_session.EventStream);
                    source.Dynamic.All += DynamicAllMonitor;
                    _renderer.EventPipeSourceConnected();
                    source.Process();
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
            bool shouldStopAfterDuration = _duration != default(TimeSpan);
            Stopwatch durationStopwatch = null;

            if (shouldStopAfterDuration)
            {
                durationStopwatch = Stopwatch.StartNew();
            }

            while (!_shouldExit.Task.Wait(250))
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

                if (shouldStopAfterDuration && durationStopwatch.Elapsed >= _duration)
                {
                    durationStopwatch.Stop();
                    break;
                }
            }

            StopMonitor();
            return _shouldExit.Task;
        }
    }
}
