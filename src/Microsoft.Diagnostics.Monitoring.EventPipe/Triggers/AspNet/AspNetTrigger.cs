// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    /// <summary>
    /// Base class for all Asp.net triggers.
    /// </summary>
    internal abstract class AspNetTrigger<TSettings> : ITraceEventTrigger where TSettings : AspNetTriggerSettings
    {
        private const string ActivityId = "activityid";
        private const string Path = "path";
        private const string StatusCode = "statuscode";
        private const string ActivityDuration = "activityduration";
        private const string Activity1Start = "Activity1/Start";
        private const string Activity1Stop = "Activity1/Stop";
        private static readonly Guid MicrosoftAspNetCoreHostingGuid = new Guid("{adb401e1-5296-51f8-c125-5fda75826144}");
        private static readonly Dictionary<string, IReadOnlyCollection<string>> _providerMap = new()
            {
                { MonitoringSourceConfiguration.DiagnosticSourceEventSource, new[]{ Activity1Start, Activity1Stop } },
                { MonitoringSourceConfiguration.MicrosoftAspNetCoreHostingEventSourceName, new[]{ "EventCounters" } }
            };

        private readonly GlobMatcher _matcher;

        protected AspNetTrigger(TSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Validate(settings);

            _matcher = new GlobMatcher(settings.IncludePaths, settings.ExcludePaths);
        }

        private static void Validate(TSettings settings)
        {
            ValidationContext context = new(settings);
            Validator.ValidateObject(settings, context, validateAllProperties: true);
        }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetProviderEventMap() => _providerMap;

        public TSettings Settings { get; }

        protected virtual bool ActivityStart(DateTime timestamp, string activityId) => false;

        protected virtual bool ActivityStop(DateTime timestamp, string activityId, long durationTicks, int statusCode) => false;

        protected virtual bool Heartbeat(DateTime timestamp) => false;

        public bool HasSatisfiedCondition(TraceEvent traceEvent)
        {
            //We deconstruct the TraceEvent data to make it easy to write tests
            DateTime timeStamp = traceEvent.TimeStamp;

            if (traceEvent.ProviderGuid == MicrosoftAspNetCoreHostingGuid)
            {
                int? statusCode = null;
                long? duration = null;
                string activityId = string.Empty;
                string path = string.Empty;

                AspnetTriggerEventType eventType = AspnetTriggerEventType.Start;

                System.Collections.IList arguments = (System.Collections.IList)traceEvent.PayloadValue(2);

                foreach (var argument in arguments)
                {
                    if (argument is IEnumerable<KeyValuePair<string, object>> argumentsEnumerable)
                    {
                        string key = (string)argumentsEnumerable.First().Value;
                        string value = (string)argumentsEnumerable.Last().Value;

                        if (key.Equals(ActivityId, StringComparison.OrdinalIgnoreCase))
                        {
                            activityId = value;
                        }
                        else if (key.Equals(Path, StringComparison.OrdinalIgnoreCase))
                        {
                            path = value;
                        }
                        else if (key.Equals(StatusCode, StringComparison.OrdinalIgnoreCase))
                        {
                            statusCode = int.Parse(value);
                        }
                        else if (key.Equals(ActivityDuration, StringComparison.OrdinalIgnoreCase))
                        {
                            duration = long.Parse(value);
                        }
                    }
                }

                if (traceEvent.EventName == Activity1Stop)
                {
                    eventType = AspnetTriggerEventType.Stop;

                    Debug.Assert(statusCode != null, "Status code cannot be null.");
                    Debug.Assert(duration != null, "Duration cannot be null.");
                }

                return HasSatisfiedCondition(timeStamp, eventType, activityId, path, statusCode, duration);
            }

            //Heartbeat only
            return HasSatisfiedCondition(timeStamp, eventType: AspnetTriggerEventType.Heartbeat, activityId: null, path: null, statusCode: null, duration: null);
        }

        /// <summary>
        /// This method is to enable testing.
        /// </summary>
        internal bool HasSatisfiedCondition(DateTime timestamp, AspnetTriggerEventType eventType, string activityId, string path, int? statusCode, long? duration)
        {
            if (eventType == AspnetTriggerEventType.Heartbeat)
            {
                return Heartbeat(timestamp);
            }

            if (!_matcher.Match(path))
            {
                //No need to update counts if the path is excluded.
                return false;
            }

            if (eventType == AspnetTriggerEventType.Start)
            {
                return ActivityStart(timestamp, activityId);
            }
            else if (eventType == AspnetTriggerEventType.Stop)
            {
                return ActivityStop(timestamp, activityId, duration.Value, statusCode.Value);
            }
            return false;
        }
    }

    internal enum AspnetTriggerEventType
    {
        Start,
        Stop,
        Heartbeat
    }
}
