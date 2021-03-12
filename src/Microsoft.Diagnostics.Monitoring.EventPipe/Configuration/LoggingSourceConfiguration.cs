// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class LoggingSourceConfiguration : MonitoringSourceConfiguration
    {
        private const string UseAppFilters = "UseAppFilters";

        private readonly LogLevel _level;
        private readonly bool _useAppFilters;

        /// <summary>
        /// Creates a new logging source configuration.
        /// </summary>
        /// <param name="level">The logging level. Log messages at or above the log level will be included.</param>
        /// <param name="useAppFilters">Use the UseAppFilters filterspec. This supersedes the log level and generates
        /// log messages with the same levels per category as specified by the application configuration.</param>
        public LoggingSourceConfiguration(LogLevel level = LogLevel.Debug, bool useAppFilters = false)
        {
            _level = level;
            _useAppFilters = useAppFilters;
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            string filterSpec = _useAppFilters ? UseAppFilters : FormattableString.Invariant($"*:{_level:G}");

            var providers = new List<EventPipeProvider>()
            {

                // Logging
                new EventPipeProvider(
                    MicrosoftExtensionsLoggingProviderName,
                    EventLevel.LogAlways,
                    (long)(LoggingEventSource.Keywords.JsonMessage | LoggingEventSource.Keywords.FormattedMessage),
                    arguments: new Dictionary<string,string>
                        {

                            { "FilterSpecs", filterSpec }
                        }
                )
            };

            return providers;
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
