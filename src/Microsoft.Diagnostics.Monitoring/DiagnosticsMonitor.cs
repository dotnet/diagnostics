using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsWPF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PEFile;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TraceReloggerLib;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticsMonitor : IAsyncDisposable
    {
        private readonly IServiceProvider _services;
        private readonly Microsoft.Extensions.Logging.ILogger<DiagnosticsMonitor> _logger;
        private readonly MonitoringSourceConfiguration _sourceConfig;
        private readonly IEnumerable<IMetricsLogger> _metricLoggers;

        //These values don't change so we compute them only once
        private readonly List<string> _dimValues;

        public const string NamespaceName = "Namespace";
        public const string NodeName = "Node";
        private static readonly List<string> DimNames = new List<string>{ NamespaceName, NodeName};

        private int _disposeState = 0;

        public DiagnosticsMonitor(IServiceProvider services, MonitoringSourceConfiguration sourceConfig)
        {
            _services = services;
            _sourceConfig = sourceConfig;
            IOptions<ContextConfiguration> contextConfig = _services.GetService<IOptions<ContextConfiguration>>();
            _dimValues = new List<string> { contextConfig.Value.Namespace, contextConfig.Value.Node };
            _metricLoggers = _services.GetServices<IMetricsLogger>();
            _logger = _services.GetService<ILogger<DiagnosticsMonitor>>();
        }

        public async Task ProcessEvents(int processId, CancellationToken cancellationToken)
        {
            var hasEventPipe = false;

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

                    // Metrics
                    HandleEventCounters(source);

                    // Logging
                    HandleLoggingEvents(source);

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

        private void HandleLoggingEvents(EventPipeEventSource source)
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
            if (Interlocked.CompareExchange(ref _disposeState, 1, 0) == 1)
            {
                return;
            }
            
            foreach(IMetricsLogger logger in _metricLoggers)
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