// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Text;

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
            if (_window.Count >= Settings.RequestCount)
            {
                //Reset trigger
                _window.Clear();
                return true;
            }

            return base.ActivityStart(timestamp, activityId);
        }
    }
}
