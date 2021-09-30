﻿// Licensed to the .NET Foundation under one or more agreements.
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

        public AspNetRequestStatusTrigger(AspNetRequestStatusTriggerSettings settings) : base(settings)
        {
            _window = new SlidingWindow(settings.SlidingWindowDuration);
        }

        protected override bool ActivityStop(DateTime timestamp, string activityId, long durationTicks, int statusCode)
        {
            if (Settings.StatusCodes.Any(r => statusCode >= r.Min && statusCode <= r.Max))
            {
                _window.AddDataPoint(timestamp);
            }

            return _window.Count >= Settings.RequestCount;
        }
    }
}
