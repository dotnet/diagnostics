// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestDurationTrigger : AspNetTrigger<AspNetRequestDurationTriggerSettings>
    {
        private readonly long _durationTicks;

        //This is adjusted due to rounding errors on event counter timestamp math.
        private readonly TimeSpan _heartBeatInterval = TimeSpan.FromSeconds(AspNetTriggerSourceConfiguration.DefaultHeartbeatInterval - 1);
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

            return base.ActivityStart(timestamp, activityId);
        }

        protected override bool Heartbeat(DateTime timestamp)
        {
            //May may get additional heartbeats based on multiple counters or extra intervals. We only
            //process the data periodically.
            if (timestamp - _lastHeartbeatProcessed > _heartBeatInterval)
            {
                _lastHeartbeatProcessed = timestamp;
                List<string> requestsToRemove = new();
                bool trigger = false;

                foreach (KeyValuePair<string, DateTime> request in _requests)
                {
                    if ((timestamp - request.Value) >= Settings.RequestDuration)
                    {
                        _window.AddDataPoint(timestamp);

                        //We don't want to count the request more than once, since it could still finish later.
                        //At this point we already deeemed it too slow. We also want to make sure we
                        //clear the cached requests periodically even if they don't finish.
                        requestsToRemove.Add(request.Key);
                        if (_window.Count >= Settings.RequestCount)
                        {
                            trigger = true;
                            break;
                        }
                    }
                }

                foreach(string requestId in requestsToRemove)
                {
                    _requests.Remove(requestId);
                }

                if (trigger)
                {
                    //Reset trigger
                    _window.Clear();
                    return true;
                }
            }

            return base.Heartbeat(timestamp);
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

            if (_window.Count >= Settings.RequestCount)
            {
                //Reset trigger
                _window.Clear();
                return true;
            }

            return base.ActivityStop(timestamp, activityId, durationTicks, statusCode);
        }
    }
}
