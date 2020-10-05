// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Graphs;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Extensions;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal partial class DiagnosticsEventPipeProcessor : IAsyncDisposable
    {
        private readonly MemoryGraph _gcGraph;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IEnumerable<IMetricsLogger> _metricLoggers;
        private readonly PipeMode _mode;
        private readonly int _metricIntervalSeconds;
        private readonly CounterFilter _counterFilter;
        private readonly LogLevel _logsLevel;
        private readonly Func<string, Task> _processInfoCallback;
        private readonly MonitoringSourceConfiguration _userConfig;
        private readonly Func<Stream, CancellationToken, Task> _streamAvailable;

        private readonly object _lock = new object();

        private TaskCompletionSource<bool> _sessionStarted;
        private EventPipeEventSource _eventPipeSession;
        private bool _disposed;

        public DiagnosticsEventPipeProcessor(
            PipeMode mode,
            ILoggerFactory loggerFactory = null,              // PipeMode = Logs
            LogLevel logsLevel = LogLevel.Debug,              // PipeMode = Logs
            IEnumerable<IMetricsLogger> metricLoggers = null, // PipeMode = Metrics
            int metricIntervalSeconds = 10,                   // PipeMode = Metrics
            CounterFilter metricFilter = null,                // PipeMode = Metrics
            MemoryGraph gcGraph = null,                       // PipeMode = GCDump
            MonitoringSourceConfiguration configuration = null, // PipeMode = Nettrace
            Func<Stream, CancellationToken, Task> streamAvailable = null, // PipeMode = Nettrace
            Func<string, Task> processInfoCallback = null     // PipeMode = ProcessInfo
            )
        {
            _metricLoggers = metricLoggers ?? Enumerable.Empty<IMetricsLogger>();
            _mode = mode;
            _loggerFactory = loggerFactory;
            _gcGraph = gcGraph;
            _metricIntervalSeconds = metricIntervalSeconds;
            _logsLevel = logsLevel;
            _processInfoCallback = processInfoCallback;
            _userConfig = configuration;
            _streamAvailable = streamAvailable;
            _processInfoCallback = processInfoCallback;
            _counterFilter = metricFilter;

            _sessionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task Process(DiagnosticsClient client, int pid, TimeSpan duration, CancellationToken token)
        {
            //No need to guard against reentrancy here, since the calling pipeline does this already.
            IDisposable registration = token.Register(() => _sessionStarted.TrySetCanceled());
            await await Task.Factory.StartNew(async () =>
            {
                EventPipeEventSource source = null;
                DiagnosticsMonitor monitor = null;
                Task handleEventsTask = Task.CompletedTask;
                try
                {
                    MonitoringSourceConfiguration config = null;
                    if (_mode == PipeMode.Logs)
                    {
                        config = new LoggingSourceConfiguration(_logsLevel);
                    }
                    else if (_mode == PipeMode.Metrics)
                    {
                        config = new MetricSourceConfiguration(_metricIntervalSeconds, _counterFilter.GetProviders());
                    }
                    else if (_mode == PipeMode.GCDump)
                    {
                        config = new GCDumpSourceConfiguration();
                    }
                    else if (_mode == PipeMode.ProcessInfo)
                    {
                        config = new SampleProfilerConfiguration();
                    }
                    else if (_mode == PipeMode.Nettrace)
                    {
                        config = _userConfig;
                    }

                    monitor = new DiagnosticsMonitor(config);
                    Stream sessionStream = await monitor.ProcessEvents(client, duration, token);

                    if (_mode == PipeMode.Nettrace)
                    {
                        _sessionStarted.TrySetResult(true);
                        await _streamAvailable(sessionStream, token);
                        return;
                    }

                    source = new EventPipeEventSource(sessionStream);

                    // Allows the event handling routines to stop processing before the duration expires.
                    Func<Task> stopFunc = () => Task.Run(() => { monitor.StopProcessing(); });

                    if (_mode == PipeMode.Metrics)
                    {
                        // Metrics
                        HandleEventCounters(source);
                    }
                    else if (_mode == PipeMode.Logs)
                    {
                        // Logging
                        HandleLoggingEvents(source);
                    }
                    else if (_mode == PipeMode.GCDump)
                    {
                        // GC
                        handleEventsTask = HandleGCEvents(source, pid, stopFunc, token);
                    }

                    else if (_mode == PipeMode.ProcessInfo)
                    {
                        // ProcessInfo
                        handleEventsTask = HandleProcessInfo(source, stopFunc, token);
                    }

                    lock(_lock)
                    {
                        _eventPipeSession = source;
                    }
                    registration.Dispose();
                    _sessionStarted.TrySetResult(true);

                    source.Process();
                    token.ThrowIfCancellationRequested();
                }
                catch (DiagnosticsClientException ex)
                {
                    throw new InvalidOperationException("Failed to start the event pipe session", ex);
                }
                finally
                {
                    ExecuteMetricLoggerAction((metricLogger) => metricLogger.PipelineStopped());

                    registration.Dispose();
                    EventPipeEventSource session = null;
                    lock (_lock)
                    {
                        session = _eventPipeSession;
                        _eventPipeSession = null;
                    }

                    session?.Dispose();
                    if (monitor != null)
                    {
                        await monitor.DisposeAsync();
                    }
                }

                // Await the task returned by the event handling method AFTER the EventPipeEventSource is disposed.
                // The EventPipeEventSource will only raise the Completed event when it is disposed. So if this task
                // is waiting for the Completed event to be raised, it will never complete until after EventPipeEventSource
                // is diposed.
                await handleEventsTask;

            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public async Task StopProcessing(CancellationToken token)
        {
            await _sessionStarted.Task;

            EventPipeEventSource session = null;
            lock (_lock)
            {
                session = _eventPipeSession;
            }
            if (session != null)
            {
                session.StopProcessing();
            }
        }

        private void HandleLoggingEvents(EventPipeEventSource source)
        {
            string lastFormattedMessage = string.Empty;

            var logActivities = new Dictionary<Guid, LogActivityItem>();
            var stack = new Stack<Guid>();

            source.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJson/Start", (traceEvent) =>
            {
                var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                var argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

                // TODO: Store this information by logger factory id
                var item = new LogActivityItem
                {
                    ActivityID = traceEvent.ActivityID,
                    ScopedObject = new LogObject(JsonDocument.Parse(argsJson).RootElement),
                };

                if (stack.Count > 0)
                {
                    Guid parentId = stack.Peek();
                    if (logActivities.TryGetValue(parentId, out var parentItem))
                    {
                        item.Parent = parentItem;
                    }
                }

                stack.Push(traceEvent.ActivityID);
                logActivities[traceEvent.ActivityID] = item;
            });

            source.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJson/Stop", (traceEvent) =>
            {
                var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                var categoryName = (string)traceEvent.PayloadByName("LoggerName");

                stack.Pop();
                logActivities.Remove(traceEvent.ActivityID);
            });

            source.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "MessageJson", (traceEvent) =>
            {
                // Level, FactoryID, LoggerName, EventID, EventName, ExceptionJson, ArgumentsJson
                var logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                var eventId = (int)traceEvent.PayloadByName("EventId");
                var eventName = (string)traceEvent.PayloadByName("EventName");
                var exceptionJson = (string)traceEvent.PayloadByName("ExceptionJson");
                var argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

                // There's a bug that causes some of the columns to get mixed up
                if (eventName.StartsWith("{"))
                {
                    argsJson = exceptionJson;
                    exceptionJson = eventName;
                    eventName = null;
                }

                if (string.IsNullOrEmpty(argsJson))
                {
                    return;
                }

                Exception exception = null;

                ILogger logger = _loggerFactory.CreateLogger(categoryName);
                var scopes = new List<IDisposable>();

                if (logActivities.TryGetValue(traceEvent.ActivityID, out var logActivityItem))
                {
                    // REVIEW: Does order matter here? We're combining everything anyways.
                    while (logActivityItem != null)
                    {
                        scopes.Add(logger.BeginScope(logActivityItem.ScopedObject));

                        logActivityItem = logActivityItem.Parent;
                    }
                }

                try
                {
                    if (exceptionJson != "{}")
                    {
                        var exceptionMessage = JsonSerializer.Deserialize<JsonElement>(exceptionJson);
                        exception = new LoggerException(exceptionMessage);
                    }

                    var message = JsonSerializer.Deserialize<JsonElement>(argsJson);
                    if (message.TryGetProperty("{OriginalFormat}", out var formatElement))
                    {
                        var formatString = formatElement.GetString();
                        var formatter = new LogValuesFormatter(formatString);
                        object[] args = new object[formatter.ValueNames.Count];
                        for (int i = 0; i < args.Length; i++)
                        {
                            args[i] = message.GetProperty(formatter.ValueNames[i]).GetString();
                        }

                        logger.Log(logLevel, new EventId(eventId, eventName), exception, formatString, args);
                    }
                    else
                    {
                        var obj = new LogObject(message, lastFormattedMessage);
                        logger.Log(logLevel, new EventId(eventId, eventName), obj, exception, LogObject.Callback);
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    scopes.ForEach(d => d.Dispose());
                }
            });

            source.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) =>
            {
                // Level, FactoryID, LoggerName, EventID, EventName, FormattedMessage
                var logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                var eventId = (int)traceEvent.PayloadByName("EventId");
                var eventName = (string)traceEvent.PayloadByName("EventName");
                var formattedMessage = (string)traceEvent.PayloadByName("FormattedMessage");

                if (string.IsNullOrEmpty(formattedMessage))
                {
                    formattedMessage = eventName;
                    eventName = string.Empty;
                }

                lastFormattedMessage = formattedMessage;
            });
        }

        private void HandleEventCounters(EventPipeEventSource source)
        {
            ExecuteMetricLoggerAction((metricLogger) => metricLogger.PipelineStarted());

            source.Dynamic.All += traceEvent =>
            {
                try
                {
                    // Metrics
                    if (traceEvent.EventName.Equals("EventCounters"))
                    {
                        IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                        IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                        //Make sure we are part of the requested series. If multiple clients request metrics, all of them get the metrics.
                        string series = payloadFields["Series"].ToString();
                        if (GetInterval(series) != _metricIntervalSeconds * 1000)
                        {
                            return;
                        }

                        string counterName = payloadFields["Name"].ToString();
                        if (!_counterFilter.Include(traceEvent.ProviderName, counterName))
                        {
                            return;
                        }

                        float intervalSec = (float)payloadFields["IntervalSec"];
                        string displayName = payloadFields["DisplayName"].ToString();
                        string displayUnits = payloadFields["DisplayUnits"].ToString();
                        double value = 0;
                        MetricType metricType = MetricType.Avg;

                        if (payloadFields["CounterType"].Equals("Mean"))
                        {
                            value = (double)payloadFields["Mean"];
                        }
                        else if (payloadFields["CounterType"].Equals("Sum"))
                        {
                            metricType = MetricType.Sum;
                            value = (double)payloadFields["Increment"];
                            if (string.IsNullOrEmpty(displayUnits))
                            {
                                displayUnits = "count";
                            }
                            //TODO Should we make these /sec like the dotnet-counters tool?
                        }

                        // Note that dimensional data such as pod and namespace are automatically added in prometheus and azure monitor scenarios.
                        // We no longer added it here.
                        var metric = new Metric(traceEvent.TimeStamp,
                            traceEvent.ProviderName,
                            counterName, displayName,
                            displayUnits,
                            value,
                            metricType,
                            intervalSec);

                        ExecuteMetricLoggerAction((metricLogger) => metricLogger.LogMetrics(metric));
                    }
                }
                catch (Exception)
                {
                }
            };
        }

        private static int GetInterval(string series)
        {
            const string comparison = "Interval=";
            int interval = 0;
            if (series.StartsWith(comparison, StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(series.Substring(comparison.Length), out interval);
            }
            return interval;
        }

        private void ExecuteMetricLoggerAction(Action<IMetricsLogger> action)
        {
            foreach (IMetricsLogger metricLogger in _metricLoggers)
            {
                try
                {
                    action(metricLogger);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private async Task HandleGCEvents(EventPipeEventSource source, int pid, Func<Task> stopFunc, CancellationToken token)
        {
            int gcNum = -1;

            Action<GCStartTraceData, Action> gcStartHandler = (GCStartTraceData data, Action taskComplete) =>
            {
                if (data.ProcessID != pid)
                {
                    return;
                }

                taskComplete();

                if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                {
                    gcNum = data.Count;
                }
            };

            Action<GCBulkNodeTraceData, Action> gcBulkNodeHandler = (GCBulkNodeTraceData data, Action taskComplete) =>
            {
                if (data.ProcessID != pid)
                {
                    return;
                }

                taskComplete();
            };

            Action<GCEndTraceData, Action> gcEndHandler = (GCEndTraceData data, Action taskComplete) =>
            {
                if (data.ProcessID != pid)
                {
                    return;
                }

                if (data.Count == gcNum)
                {
                    taskComplete();
                }
            };

            // Register event handlers on the event source and represent their completion as tasks
            using var gcStartTaskSource = new EventTaskSource<Action<GCStartTraceData>>(
                taskComplete => data => gcStartHandler(data, taskComplete),
                handler => source.Clr.GCStart += handler,
                handler => source.Clr.GCStart -= handler,
                token);
            using var gcBulkNodeTaskSource = new EventTaskSource<Action<GCBulkNodeTraceData>>(
                taskComplete => data => gcBulkNodeHandler(data, taskComplete),
                handler => source.Clr.GCBulkNode += handler,
                handler => source.Clr.GCBulkNode -= handler,
                token);
            using var gcStopTaskSource = new EventTaskSource<Action<GCEndTraceData>>(
                taskComplete => data => gcEndHandler(data, taskComplete),
                handler => source.Clr.GCStop += handler,
                handler => source.Clr.GCStop -= handler,
                token);
            using var sourceCompletedTaskSource = new EventTaskSource<Action>(
                taskComplete => taskComplete,
                handler => source.Completed += handler,
                handler => source.Completed -= handler,
                token);

            // A task that is completed whenever GC data is received
            Task gcDataTask = Task.WhenAny(gcStartTaskSource.Task, gcBulkNodeTaskSource.Task);
            Task gcStopTask = gcStopTaskSource.Task;

            DotNetHeapDumpGraphReader dumper = new DotNetHeapDumpGraphReader(TextWriter.Null)
            {
                DotNetHeapInfo = new DotNetHeapInfo()
            };
            dumper.SetupCallbacks(_gcGraph, source, pid.ToString(CultureInfo.InvariantCulture));

            // The event source will not always provide the GC events when it starts listening. However,
            // they will be provided when the event source is told to stop processing events. Give the
            // event source some time to produce the events, but if it doesn't start producing them within
            // a short amount of time (5 seconds), then stop processing events to allow them to be flushed.
            Task eventsTimeoutTask = Task.Delay(TimeSpan.FromSeconds(5), token);
            Task completedTask = await Task.WhenAny(gcDataTask, eventsTimeoutTask);

            token.ThrowIfCancellationRequested();

            // If started receiving GC events, wait for the GC Stop event.
            if (completedTask == gcDataTask)
            {
                await gcStopTask;
            }

            // Stop receiving events; if haven't received events yet, this will force flushing of events.
            await stopFunc();

            // Wait for all received events to be processed.
            await sourceCompletedTaskSource.Task;

            // Check that GC data and stop events were received. This is done by checking that the
            // associated tasks have ran to completion. If one of them has not reached the completion state, then
            // fail the GC dump operation.
            if (gcDataTask.Status != TaskStatus.RanToCompletion ||
                gcStopTask.Status != TaskStatus.RanToCompletion)
            {
                throw new InvalidOperationException("Unable to create GC dump due to incomplete GC data.");
            }

            dumper.ConvertHeapDataToGraph();

            _gcGraph.AllowReading();
        }

        private async Task HandleProcessInfo(EventPipeEventSource source, Func<Task> stopFunc, CancellationToken token)
        {
            string commandLine = null;
            Action<TraceEvent, Action> processInfoHandler = (TraceEvent traceEvent, Action taskComplete) =>
            {
                commandLine = (string)traceEvent.PayloadByName("CommandLine");
                taskComplete();
            };

            // Completed when the ProcessInfo event of the Microsoft-DotNETCore-EventPipe event provider is handled
            using var processInfoTaskSource = new EventTaskSource<Action<TraceEvent>>(
                taskComplete => traceEvent => processInfoHandler(traceEvent, taskComplete),
                handler => source.Dynamic.AddCallbackForProviderEvent(MonitoringSourceConfiguration.EventPipeProviderName, "ProcessInfo", handler),
                handler => source.Dynamic.RemoveCallback(handler),
                token);

            // Completed when any trace event is handled
            using var anyEventTaskSource = new EventTaskSource<Action<TraceEvent>>(
                taskComplete => traceEvent => taskComplete(),
                handler => source.Dynamic.All += handler,
                handler => source.Dynamic.All -= handler,
                token);

            // Wait for any trace event to be processed
            await anyEventTaskSource.Task;

            // Stop the event pipe session
            await stopFunc();

            // Wait for the ProcessInfo event to be processed
            await processInfoTaskSource.Task;

            // Notify of command line information
            await _processInfoCallback(commandLine);
        }

        public async ValueTask DisposeAsync()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }

            _sessionStarted.TrySetCanceled();
            try
            {
                await _sessionStarted.Task;
            }
            catch
            {
            }

            _eventPipeSession?.Dispose();

            foreach (IMetricsLogger logger in _metricLoggers)
            {
                if (logger is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    logger?.Dispose();
                }
            }
        }

        private class LogActivityItem
        {
            public Guid ActivityID { get; set; }

            public LogObject ScopedObject { get; set; }

            public LogActivityItem Parent { get; set; }
        }
    }
}
