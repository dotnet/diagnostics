// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventLogsPipeline : EventSourcePipeline<EventLogsPipelineSettings>
    {
        private readonly ILoggerFactory _factory;
        private static readonly Func<object, Exception, string> _messageFormatter = MessageFormatter;
        public EventLogsPipeline(DiagnosticsClient client, EventLogsPipelineSettings settings, ILoggerFactory factory)
            : base(client, settings)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            try
            {
                return new LoggingSourceConfiguration(
                    Settings.LogLevel,
                    LogMessageType.FormattedMessage | LogMessageType.JsonMessage,
                    Settings.FilterSpecs,
                    Settings.UseAppFilters);
            }
            catch (NotSupportedException ex)
            {
                throw new PipelineException(ex.Message, ex);
            }
        }

        protected override Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            string lastFormattedMessage = string.Empty;

            Dictionary<Guid, LogActivityItem> logActivities = new();
            Stack<Guid> stack = new();

            eventSource.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJson/Start", (traceEvent) => {
                int factoryId = (int)traceEvent.PayloadByName("FactoryID");
                string categoryName = (string)traceEvent.PayloadByName("LoggerName");
                string argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

                // TODO: Store this information by logger factory id
                LogActivityItem item = new()
                {
                    ActivityID = traceEvent.ActivityID,
                    ScopedObject = new LogObject(JsonDocument.Parse(argsJson).RootElement),
                };

                if (stack.Count > 0)
                {
                    Guid parentId = stack.Peek();
                    if (logActivities.TryGetValue(parentId, out LogActivityItem parentItem))
                    {
                        item.Parent = parentItem;
                    }
                }

                stack.Push(traceEvent.ActivityID);
                logActivities[traceEvent.ActivityID] = item;
            });

            eventSource.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJson/Stop", (traceEvent) => {
                int factoryId = (int)traceEvent.PayloadByName("FactoryID");
                string categoryName = (string)traceEvent.PayloadByName("LoggerName");

                //If we begin collection in the middle of a request, we can receive a stop without having a start.
                if (stack.Count > 0)
                {
                    stack.Pop();
                    logActivities.Remove(traceEvent.ActivityID);
                }
            });

            eventSource.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "MessageJson", (traceEvent) => {
                // Level, FactoryID, LoggerName, EventID, EventName, ExceptionJson, ArgumentsJson
                LogLevel logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                int factoryId = (int)traceEvent.PayloadByName("FactoryID");
                string categoryName = (string)traceEvent.PayloadByName("LoggerName");
                int eventId = (int)traceEvent.PayloadByName("EventId");
                string eventName = (string)traceEvent.PayloadByName("EventName");
                string exceptionJson = (string)traceEvent.PayloadByName("ExceptionJson");
                string argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

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

                ILogger logger = _factory.CreateLogger(categoryName);
                List<IDisposable> scopes = new();

                if (logActivities.TryGetValue(traceEvent.ActivityID, out LogActivityItem logActivityItem))
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
                        JsonElement exceptionMessage = JsonSerializer.Deserialize<JsonElement>(exceptionJson);
                        exception = new LoggerException(exceptionMessage);
                    }

                    JsonElement message = JsonSerializer.Deserialize<JsonElement>(argsJson);

                    const string OriginalFormatProperty = "{OriginalFormat}";
                    bool parsedState = false;
                    if (message.TryGetProperty(OriginalFormatProperty, out JsonElement formatElement))
                    {
                        string formatString = formatElement.GetString();
                        LogValuesFormatter formatter = new(formatString);
                        object[] args = new object[formatter.ValueNames.Count];

                        // NOTE: Placeholders in log messages are ordinal based, names are not used to align the arguments to placeholders.
                        // This means a placeholder name can be used multiple times in a single message.
                        using JsonElement.ObjectEnumerator argsEnumerator = message.EnumerateObject();
                        if (argsEnumerator.MoveNext())
                        {
                            JsonProperty currentElement = argsEnumerator.Current;
                            parsedState = true;

                            // NOTE: In general there'll be N+1 properties in the arguments payload, where the last entry is the original format string.
                            //
                            // It's possible that a log message with placeholders will supply a null array for the arguments.
                            // In which case there will only be the original format string in the arguments payload
                            // and we can skip filling the args array as all values should be null.
                            if (!string.Equals(OriginalFormatProperty, currentElement.Name, StringComparison.Ordinal))
                            {
                                for (int i = 0; i < formatter.ValueNames.Count; i++)
                                {
                                    args[i] = currentElement.Value.GetString();

                                    if (!argsEnumerator.MoveNext())
                                    {
                                        parsedState = false;
                                        break;
                                    }
                                    currentElement = argsEnumerator.Current;
                                }
                            }

                            if (parsedState)
                            {
                                //We want to propagate the timestamp to the underlying logger, but that's not part of the ILogger interface.
                                //We replicate LoggerExtensions.Log, but add an interface capability to the object
                                //CONSIDER FormattedLogValues maintains a cache of formatters. We are effectively duplicating this cache.
                                FormattedLogValues logValues = new(traceEvent.TimeStamp, formatString, args);
                                logger.Log(logLevel, new EventId(eventId, eventName), logValues, exception, _messageFormatter);
                            }
                        }
                    }

                    if (!parsedState)
                    {
                        LogObject obj = new(message, lastFormattedMessage) { Timestamp = traceEvent.TimeStamp };
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

            eventSource.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) => {
                // Level, FactoryID, LoggerName, EventID, EventName, FormattedMessage
                LogLevel logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                int factoryId = (int)traceEvent.PayloadByName("FactoryID");
                string categoryName = (string)traceEvent.PayloadByName("LoggerName");
                int eventId = (int)traceEvent.PayloadByName("EventId");
                string eventName = (string)traceEvent.PayloadByName("EventName");
                string formattedMessage = (string)traceEvent.PayloadByName("FormattedMessage");

                if (string.IsNullOrEmpty(formattedMessage))
                {
                    formattedMessage = eventName;
                    eventName = string.Empty;
                }

                lastFormattedMessage = formattedMessage;
            });

            return Task.CompletedTask;
        }

        private static string MessageFormatter(object state, Exception error)
        {
            return state.ToString();
        }

        private class LogActivityItem
        {
            public Guid ActivityID { get; set; }

            public LogObject ScopedObject { get; set; }

            public LogActivityItem Parent { get; set; }
        }
    }
}
