using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsWPF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PEFile;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticsMonitor : IDisposable
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        private readonly MonitoringSourceConfiguration _sourceConfig;
        private readonly ContextConfiguration _context;
        private readonly List<MonitoringSinkConfiguration> _sinkConfig;

        //TODO Make this DI
        private List<IMetricsLogger> _metricLoggers;

        //TODO localize?
        private static readonly List<string> DimNames = new List<string>{ "Namespace", "Node"};

        //These values don't change so we compute them only once
        private readonly List<string> _dimValues = new List<string> {string.Empty, string.Empty};

        public DiagnosticsMonitor(ContextConfiguration context, MonitoringSourceConfiguration sourceConfig,
            IEnumerable<MonitoringSinkConfiguration> sinkConfig, Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
            _sourceConfig = sourceConfig;
            _context = context;
            _sinkConfig = new List<MonitoringSinkConfiguration>(sinkConfig);
            _metricLoggers = new List<IMetricsLogger>();
        }

        public async Task ProcessEvents(int processId, CancellationToken cancellationToken)
        {
            var hasEventPipe = false;

            _dimValues[0] = _context.Namespace;
            _dimValues[1] = _context.Node;

            for (int i = 0; i < 10; ++i)
            {
                if (DiagnosticsClient.GetPublishedProcesses().Contains(processId))
                {
                    hasEventPipe = true;
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(500);
            }

            if (!hasEventPipe)
            {
                _logger.LogInformation("Process id {PID}, does not support event pipe", processId);
                return;
            }

            _logger.LogInformation("Listening for event pipe events for {ServiceName} on process id {PID}", _dimValues[1], processId);

            while (!cancellationToken.IsCancellationRequested)
            {
                EventPipeSession session = null;
                var client = new DiagnosticsClient(processId);

                try
                {
                    session = client.StartEventPipeSession(_sourceConfig.GetProviders());
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                    }

                    // We can't even start the session, wait until the process boots up again to start another metrics thread
                    break;
                }

                void StopSession()
                {
                    try
                    {
                        session.Stop();
                    }
                    catch (EndOfStreamException)
                    {
                        // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
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
                }

                using var _ = cancellationToken.Register(() => StopSession());

                try
                {
                    var source = new EventPipeEventSource(session.EventStream);

                    // We rely on Dependency Injection to create the ILogger instances and lifetimes
                    ILoggerFactory loggerFactory = LoggerFactory.Create((ILoggingBuilder builder) => 
                    {
                        foreach(MonitoringSinkConfiguration config in _sinkConfig)
                        {
                            config.AddLogger(builder);
                        }
                    });

                    foreach (MonitoringSinkConfiguration config in _sinkConfig)
                    {
                        config.AddMetricsLogger(_metricLoggers);
                    }

                    // Metrics
                    HandleEventCounters(source);

                    // Logging
                    HandleLoggingEvents(source, loggerFactory);

                    source.Process();
                }
                catch (DiagnosticsClientException ex)
                {
                    _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                }
                catch (Exception)
                {
                    // This fails if stop is called or if the process dies
                }
                finally
                {
                    session?.Dispose();
                }
            }

            _logger.LogInformation("Event pipe collection completed for {ServiceName} on process id {PID}", _dimValues[1], processId);
        }

        private void HandleLoggingEvents(EventPipeEventSource source, ILoggerFactory loggerFactory)
        {
            string lastFormattedMessage = string.Empty;

            var logActivities = new Dictionary<Guid, LogActivityItem>();
            var stack = new Stack<Guid>();

            source.Dynamic.AddCallbackForProviderEvent(MonitoringSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJsonStart/Start", (traceEvent) =>
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

            source.Dynamic.AddCallbackForProviderEvent(MonitoringSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJsonStop/Stop", (traceEvent) =>
            {
                var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                var categoryName = (string)traceEvent.PayloadByName("LoggerName");

                stack.Pop();
                logActivities.Remove(traceEvent.ActivityID);
            });

            source.Dynamic.AddCallbackForProviderEvent(MonitoringSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "MessageJson", (traceEvent) =>
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

                ILogger logger = loggerFactory.CreateLogger(categoryName);

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
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error processing log entry for {ServiceName}", _dimValues[1]);
                }
                finally
                {
                    scopes.ForEach(d => d.Dispose());
                }
            });

            source.Dynamic.AddCallbackForProviderEvent(MonitoringSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) =>
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
            source.Dynamic.All += traceEvent =>
            {
                try
                {
                    // Metrics
                    if (traceEvent.EventName.Equals("EventCounters"))
                    {
                        IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                        IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                        string counterName = payloadFields["Name"].ToString();
                        string displayName = payloadFields["DisplayName"].ToString();
                        string displayUnits = payloadFields["DisplayUnits"].ToString();
                        double value = 0;
                        if (payloadFields["CounterType"].Equals("Mean"))
                        {
                            value = (double)payloadFields["Mean"];
                        }
                        else if (payloadFields["CounterType"].Equals("Sum"))
                        {
                            value = (double)payloadFields["Increment"];
                            if (string.IsNullOrEmpty(displayUnits))
                            {
                                displayUnits = "count";
                            }
                            displayUnits += "/sec";
                        }

                        PostMetric(new Metric(traceEvent.TimeStamp, traceEvent.ProviderName, counterName, displayName, displayUnits, value, dimNames: DimNames, dimValues: _dimValues));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing counter for {ProviderName}:{EventName}", traceEvent.ProviderName, traceEvent.EventName);
                }
            };
        }

        private void PostMetric(Metric metric)
        {
            foreach(IMetricsLogger metricLogger in _metricLoggers)
            {
                metricLogger.LogMetrics(metric);
            }
        }

        public void Dispose()
        {
            if (_metricLoggers != null)
            {
                foreach(IMetricsLogger logger in _metricLoggers)
                {
                    logger?.Dispose();
                }
                _metricLoggers.Clear();
                _metricLoggers = null;
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