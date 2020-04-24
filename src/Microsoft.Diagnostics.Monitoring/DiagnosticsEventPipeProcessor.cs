// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    public enum PipeMode
    {
        Logs = 1,
        Metrics,
    }

    public class DiagnosticsEventPipeProcessor : IAsyncDisposable
    {
        private readonly IServiceProvider _services;
        private readonly Microsoft.Extensions.Logging.ILogger<DiagnosticsMonitor> _logger;
        private readonly IEnumerable<IMetricsLogger> _metricLoggers;
        private readonly PipeMode _mode;

        //These values don't change so we compute them only once
        private readonly List<string> _dimValues;

        public const string NamespaceName = "Namespace";
        public const string NodeName = "Node";
        private static readonly List<string> DimNames = new List<string> { NamespaceName, NodeName };

        public DiagnosticsEventPipeProcessor(IServiceProvider services, PipeMode mode)
        {
            _services = services;
            IOptions<ContextConfiguration> contextConfig = _services.GetService<IOptions<ContextConfiguration>>();
            _dimValues = new List<string> { contextConfig.Value.Namespace, contextConfig.Value.Node };
            _metricLoggers = _services.GetServices<IMetricsLogger>();
            _logger = _services.GetService<ILogger<DiagnosticsMonitor>>();
            _mode = mode;
        }

        public async Task Process(int pid, int duration, CancellationToken token)
        {
            await Task.Factory.StartNew(async () =>
            {
                EventPipeEventSource source = null;
                DiagnosticsMonitor monitor = null;
                try
                {
                    MonitoringSourceConfiguration config = null;
                    if (_mode == PipeMode.Logs)
                    {
                        config = new LoggingSourceConfiguration();
                    }
                    if (_mode == PipeMode.Metrics)
                    {
                        config = new MetricSourceConfiguration();
                    }

                    monitor = new DiagnosticsMonitor(_services, config);
                    Stream sessionStream = await monitor.ProcessEvents(pid, duration, token);
                    source = new EventPipeEventSource(sessionStream);
                    
                    if (_mode == PipeMode.Metrics)
                    {
                        // Metrics
                        HandleEventCounters(source);
                    }

                    if (_mode == PipeMode.Logs)
                    {
                        // Logging
                        HandleLoggingEvents(source);
                    }

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
                    source?.Dispose();
                    if (monitor != null)
                    {
                        await monitor.DisposeAsync();
                    }
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void HandleLoggingEvents(EventPipeEventSource source)
        {
            string lastFormattedMessage = string.Empty;

            var logActivities = new Dictionary<Guid, LogActivityItem>();
            var stack = new Stack<Guid>();

            source.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJsonStart/Start", (traceEvent) =>
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

            source.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJsonStop/Stop", (traceEvent) =>
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

                ILogger logger = _services.GetService<ILoggerFactory>().CreateLogger(categoryName);
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
            foreach (IMetricsLogger metricLogger in _metricLoggers)
            {
                try
                {
                    metricLogger.LogMetrics(metric);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception e)
                {
                    _logger.LogError($"Error from {metricLogger.GetType()}: {e.Message}");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
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
