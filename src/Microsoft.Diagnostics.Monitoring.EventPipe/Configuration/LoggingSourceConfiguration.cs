// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class LoggingSourceConfiguration : MonitoringSourceConfiguration
    {
        private readonly string _filterSpecs;
        private readonly long _keywords;
        private readonly EventLevel _level;

        /// <summary>
        /// Creates a new logging source configuration.
        /// </summary>
        public LoggingSourceConfiguration(LogLevel level, LogMessageType messageType, IDictionary<string, LogLevel?> filterSpecs, bool useAppFilters)
        {
            RequestRundown = false;
            _filterSpecs = ToFilterSpecsString(filterSpecs, useAppFilters);
            _keywords = (long)ToKeywords(messageType);
            _level = ToEventLevel(level);
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            return new List<EventPipeProvider>()
            {
                new EventPipeProvider(
                    MicrosoftExtensionsLoggingProviderName,
                    _level,
                    _keywords,
                    arguments: new Dictionary<string, string>
                        {
                            { "FilterSpecs", _filterSpecs }
                        }
                )
            };
        }

        private static string ToFilterSpecsString(IDictionary<string, LogLevel?> filterSpecs, bool useAppFilters)
        {
            if (!useAppFilters && (filterSpecs?.Count).GetValueOrDefault(0) == 0)
            {
                return string.Empty;
            }

            StringBuilder filterSpecsBuilder = new();

            if (useAppFilters)
            {
                filterSpecsBuilder.Append("UseAppFilters");
            }

            if (null != filterSpecs)
            {
                foreach (KeyValuePair<string, LogLevel?> filterSpec in filterSpecs)
                {
                    if (!string.IsNullOrEmpty(filterSpec.Key))
                    {
                        if (filterSpecsBuilder.Length > 0)
                        {
                            filterSpecsBuilder.Append(';');
                        }
                        filterSpecsBuilder.Append(filterSpec.Key);
                        if (filterSpec.Value.HasValue)
                        {
                            filterSpecsBuilder.Append(':');
                            filterSpecsBuilder.Append(filterSpec.Value.Value.ToString("G"));
                        }
                    }
                }
            }

            return filterSpecsBuilder.ToString();
        }

        private static EventLevel ToEventLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.None:
                    throw new NotSupportedException($"{nameof(LogLevel)} {nameof(LogLevel.None)} is not supported as the default log level.");
                case LogLevel.Trace:
                    return EventLevel.LogAlways;
                case LogLevel.Debug:
                    return EventLevel.Verbose;
                case LogLevel.Information:
                    return EventLevel.Informational;
                case LogLevel.Warning:
                    return EventLevel.Warning;
                case LogLevel.Error:
                    return EventLevel.Error;
                case LogLevel.Critical:
                    return EventLevel.Critical;
            }
            throw new InvalidOperationException($"Unable to convert {logLevel:G} to EventLevel.");
        }

        private static EventKeywords ToKeywords(LogMessageType messageType)
        {
            EventKeywords keywords = 0;
            if (messageType.HasFlag(LogMessageType.FormattedMessage))
            {
                keywords |= LoggingEventSource.Keywords.FormattedMessage;
            }
            if (messageType.HasFlag(LogMessageType.JsonMessage))
            {
                keywords |= LoggingEventSource.Keywords.JsonMessage;
            }
            if (messageType.HasFlag(LogMessageType.Message))
            {
                keywords |= LoggingEventSource.Keywords.Message;
            }
            return keywords;
        }

        private sealed class LoggingEventSource
        {
            /// <summary>
            /// This is public from an EventSource consumer point of view, but since these defintions
            /// are not needed outside this class
            /// </summary>
            public static class Keywords
            {
                /// <summary>
                /// Meta events are events about the LoggingEventSource itself (that is they did not come from ILogger
                /// </summary>
                public const EventKeywords Meta = (EventKeywords)1;
                /// <summary>
                /// Turns on the 'Message' event when ILogger.Log() is called.   It gives the information in a programmatic (not formatted) way
                /// </summary>
                public const EventKeywords Message = (EventKeywords)2;
                /// <summary>
                /// Turns on the 'FormatMessage' event when ILogger.Log() is called.  It gives the formatted string version of the information.
                /// </summary>
                public const EventKeywords FormattedMessage = (EventKeywords)4;
                /// <summary>
                /// Turns on the 'MessageJson' event when ILogger.Log() is called.   It gives  JSON representation of the Arguments.
                /// </summary>
                public const EventKeywords JsonMessage = (EventKeywords)8;
            }
        }
    }
}
