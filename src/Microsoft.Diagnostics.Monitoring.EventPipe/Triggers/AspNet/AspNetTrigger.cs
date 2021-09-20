// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    /// <summary>
    /// Base class for all Asp.net triggers.
    /// </summary>
    internal abstract class AspNetTrigger<TSettings> : ITraceEventTrigger where TSettings : AspNetTriggerSettings
    {
        private const string Activity1Start = "Activity1/Start";
        private const string Activity1Stop = "Activity1/Stop";
        private static readonly Guid MicrosoftAspNetCoreHostingGuid = new Guid("{adb401e1-5296-51f8-c125-5fda75826144}");
        private static readonly Dictionary<string, IReadOnlyCollection<string>> _providerMap = new()
            {
                { MonitoringSourceConfiguration.DiagnosticSourceEventSource, new[]{ Activity1Start, Activity1Stop } },
                { MonitoringSourceConfiguration.MicrosoftAspNetCoreHostingEventSourceName, new[]{ "EventCounters" } }
            };

        protected AspNetTrigger(TSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Validate(settings);

            IncludePaths = new HashSet<string>(Settings.IncludePaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            ExcludePaths = new HashSet<string>(Settings.ExcludePaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private static void Validate(TSettings settings)
        {
            ValidationContext context = new(settings);
            Validator.ValidateObject(settings, context, validateAllProperties: true);
        }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetProviderEventMap() => _providerMap;

        public TSettings Settings { get; }

        //CONSIDER The current design is to trigger in aggregate across all url matches.
        //If we want per path triggering, we can simply setup more triggers.
        //TODO Add support for wildcards or globs for paths
        protected HashSet<string> IncludePaths { get; }

        protected HashSet<string> ExcludePaths { get; }

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
                AspnetTriggerEventType eventType = AspnetTriggerEventType.Start;

                System.Collections.IList arguments = (System.Collections.IList)traceEvent.PayloadValue(2);
                string activityId = ExtractByIndex(arguments, 0);
                string path = ExtractByIndex(arguments, 1);

                if (traceEvent.EventName == Activity1Stop)
                {
                    statusCode = int.Parse(ExtractByIndex(arguments, 2));
                    duration = long.Parse(ExtractByIndex(arguments, 3));
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

            if (!CheckPathFilter(path))
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

        private bool CheckPathFilter(string path)
        {
            if (IncludePaths.Count > 0)
            {
                return IncludePaths.Contains(path);
            }
            if (ExcludePaths.Count > 0)
            {
                return !ExcludePaths.Contains(path);
            }
            return true;
        }

        private static string ExtractByIndex(System.Collections.IList arguments, int index)
        {
            IEnumerable<KeyValuePair<string, object>> values = (IEnumerable<KeyValuePair<string, object>>)arguments[index];
            //The data is internally organized as two KeyValuePair entries,
            //The first entry is { Key, "KeyValue"}
            //The second is { Value, "Value"}
            //e.g.
            //{{ Key:"StatusCode", Value:"200" }}
            return (string)values.Last().Value;
        }
    }

    internal enum AspnetTriggerEventType
    {
        Start,
        Stop,
        Heartbeat
    }
}
