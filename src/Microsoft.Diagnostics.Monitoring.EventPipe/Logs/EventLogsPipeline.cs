// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            //
            // We enable TplEventSource's TasksFlowActivityIds as part of our configuration to enable activity correlation.
            // This means that each time an event start occurs the current ActivityId will branch creating a new one with a RelatedActivityId equal to where it branched from.
            // Combining this with the fact that scopes are handled as ActivityJson/{Start,Stop} means the ActivityId will branch each time a scope starts.
            // When a log message occurs, it'll have an ActivityId equal to the latest applicable scope.
            //
            // By maintaining a tree with the branching data, we can construct the full scope for a log message:
            // - Each time the ActivityId branches, create a node in the tree with it's parent being the node corresponding to the RelatedActivityId.
            //   - Each node has corresponding scope data.
            // - When a log message occurs, grab the node with the corresponding ActivityId and backtrack to the root of the tree. Each node visited is included as part of the log's scope.
            //
            // NOTE: There are edge cases with concurrent traces, this is described in greater detail above our backtracking code.
            //
            Dictionary<Guid, LogScopeItem> activityIdToScope = new();

            eventSource.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJson/Start", (traceEvent) => {
                if (traceEvent.ActivityID == Guid.Empty)
                {
                    // Unexpected
                    return;
                }

                string argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

                // TODO: Store this information by logger factory id
                LogScopeItem item = new()
                {
                    ActivityID = traceEvent.ActivityID,
                    ScopedObject = new LogObject(JsonDocument.Parse(argsJson).RootElement),
                };

                if (activityIdToScope.TryGetValue(traceEvent.RelatedActivityID, out LogScopeItem parentItem))
                {
                    item.Parent = parentItem;
                }

                activityIdToScope[traceEvent.ActivityID] = item;
            });

            eventSource.Dynamic.AddCallbackForProviderEvent(LoggingSourceConfiguration.MicrosoftExtensionsLoggingProviderName, "ActivityJson/Stop", (traceEvent) => {
                // Not all stopped event ActivityIds will exist in our tree since there may be scopes already active when we start the trace session.
                _ = activityIdToScope.Remove(traceEvent.ActivityID);
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

                //
                // The MessageJson event will occur with an ActivityId equal to the most relevant activity branch and we can backtrack to the root of the tree
                // to grab all applicable scopes (where each node we visit is an applicable scope).
                //
                // Ideally the ActivityId will always exist in our tree, however if another trace is ongoing that is interested in an event start
                // within the same async context as our log message then there will be nodes+edges that our tree is unaware of.
                // This is because TplEventSource's TasksFlowActivityIds is a singleton implementation that is shared for all traces,
                // regardless of if the other traces have TasksFlowActivityIds enabled.
                //
                // In this scenario there's still a chance that only a single branch has occurred and we're the first event logged with the newly branched ActivityId,
                // in which case we can use the RelatedActivityId to still grab the whole scope.
                //
                // If not then we will be operating on a subtree without a way of getting back to the root node and will only have a subset (if any) of the
                // applicable scopes.
                //
                if (activityIdToScope.TryGetValue(traceEvent.ActivityID, out LogScopeItem scopeItem) ||
                    activityIdToScope.TryGetValue(traceEvent.RelatedActivityID, out scopeItem))
                {
                    while (scopeItem != null)
                    {
                        scopes.Add(logger.BeginScope(scopeItem.ScopedObject));

                        scopeItem = scopeItem.Parent;
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
                    if (message.TryGetProperty("{OriginalFormat}", out JsonElement formatElement))
                    {
                        string formatString = formatElement.GetString();
                        LogValuesFormatter formatter = new(formatString);
                        object[] args = new object[formatter.ValueNames.Count];
                        for (int i = 0; i < args.Length; i++)
                        {
                            if (message.TryGetProperty(formatter.ValueNames[i], out JsonElement value))
                            {
                                args[i] = value.GetString();
                            }
                        }

                        //We want to propagate the timestamp to the underlying logger, but that's not part of the ILogger interface.
                        //We replicate LoggerExtensions.Log, but add an interface capability to the object
                        //CONSIDER FormattedLogValues maintains a cache of formatters. We are effectively duplicating this cache.
                        FormattedLogValues logValues = new(traceEvent.TimeStamp, formatString, args);
                        logger.Log(logLevel, new EventId(eventId, eventName), logValues, exception, _messageFormatter);
                    }
                    else
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

        private class LogScopeItem
        {
            public Guid ActivityID { get; set; }

            public LogObject ScopedObject { get; set; }

            public LogScopeItem Parent { get; set; }
        }
    }
}
