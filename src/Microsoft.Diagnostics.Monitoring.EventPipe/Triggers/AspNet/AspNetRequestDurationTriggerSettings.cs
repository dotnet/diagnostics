// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    internal sealed class AspNetRequestDurationTriggerSettings : AspNetTriggerSettings
    {
        public const string RequestDuration_MaxValue = "01:00:00"; // 1 hour
        public const string RequestDuration_MinValue = "00:00:00"; // No minimum

        /// <summary>
        /// The minimum duration of the request to be considered slow.
        /// </summary>
        [Range(typeof(TimeSpan), RequestDuration_MinValue, RequestDuration_MaxValue)]
        public TimeSpan RequestDuration { get; set; }
    }
}
