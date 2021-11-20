// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestCountTrigger : AspNetTrigger<AspNetRequestCountTriggerSettings>
    {
        private SlidingWindow _window;

        public AspNetRequestCountTrigger(AspNetRequestCountTriggerSettings settings) : base(settings)
        {
            _window = new SlidingWindow(settings.SlidingWindowDuration);
        }

        protected override bool ActivityStart(DateTime timestamp, string activityId)
        {
            _window.AddDataPoint(timestamp);
            return _window.Count >= Settings.RequestCount;
        }
    }
}
