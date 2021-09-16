// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestStatusTrigger : AspNetTrigger<AspNetRequestStatusTriggerSettings>
    {
        private SlidingWindow _window;
        private HashSet<int> _statusCodes = new();
        private List<(int Min, int Max)> _statusCodeRanges = new();

        public AspNetRequestStatusTrigger(AspNetRequestStatusTriggerSettings settings) : base(settings)
        {
            _window = new SlidingWindow(settings.SlidingWindowDuration);

            foreach(string codeOrRange in settings.StatusCodes)
            {
                if (codeOrRange.Length == 3)
                {
                    _statusCodes.Add(int.Parse(codeOrRange));
                    continue;
                }
                string[] range = codeOrRange.Split('-');
                _statusCodeRanges.Add((int.Parse(range[0]), int.Parse(range[1])));
            }
        }

        protected override bool ActivityStop(DateTime timestamp, string activityId, long durationTicks, int statusCode)
        {
            if (_statusCodes.Contains(statusCode) ||
                _statusCodeRanges.Any(r => statusCode >= r.Min && statusCode <= r.Max))
            {
                _window.AddDataPoint(timestamp);
            }

            if (_window.Count >= Settings.RequestCount)
            {
                return true;
            }

            return false;
        }
    }
}
