// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestDurationTrigger : AspNetTrigger<AspNetRequestDurationTriggerSettings>
    {
        private readonly long _durationTicks;

        //Note that regardless of the metrics interval used, we will only update
        //on a certain frequency to avoid unnecessary processing.
        //This is adjusted due to rounding errors on event counter timestamp math.
        private static readonly TimeSpan HeartbeatIntervalSeconds = TimeSpan.FromSeconds(9);
        private SlidingWindow _window;
        private Dictionary<string, DateTime> _requests = new();
        private DateTime _lastHeartbeatProcessed = DateTime.MinValue;

        public AspNetRequestDurationTrigger(AspNetRequestDurationTriggerSettings settings) : base(settings)
        {
            _durationTicks = Settings.RequestDuration.Ticks;
            _window = new SlidingWindow(settings.SlidingWindowDuration);
        }

        protected override bool ActivityStart(DateTime timestamp, string activityId)
        {
            _requests.Add(activityId, timestamp);

            return false;
        }

        protected override bool Heartbeat(DateTime timestamp)
        {
            //May get additional heartbeats based on multiple counters or extra intervals. We only
            //process the data periodically.
            if (timestamp - _lastHeartbeatProcessed > HeartbeatIntervalSeconds)
            {
                _lastHeartbeatProcessed = timestamp;
                List<string> requestsToRemove = new();

                foreach (KeyValuePair<string, DateTime> request in _requests)
                {
                    if ((timestamp - request.Value) >= Settings.RequestDuration)
                    {
                        _window.AddDataPoint(timestamp);

                        //We don't want to count the request more than once, since it could still finish later.
                        //At this point we already deeemed it too slow. We also want to make sure we
                        //clear the cached requests periodically even if they don't finish.
                        requestsToRemove.Add(request.Key);
                    }
                }

                foreach(string requestId in requestsToRemove)
                {
                    _requests.Remove(requestId);
                }

                return _window.Count >= Settings.RequestCount;
            }

            return false;
        }

        protected override bool ActivityStop(DateTime timestamp, string activityId, long durationTicks, int statusCode)
        {
            if (!_requests.Remove(activityId))
            {
                //This request was already removed by the heartbeat. No need to evaluate duration since we don't want to double count the request.
                return false;
            }

            if (durationTicks >= _durationTicks)
            {
                _window.AddDataPoint(timestamp);
            }

            return _window.Count >= Settings.RequestCount;
        }
    }
}
