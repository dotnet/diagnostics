// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static class CounterPayloadExtensions
    {
        public static string GetDisplay(this ICounterPayload counterPayload)
        {
            if (counterPayload.CounterType == CounterType.Rate)
            {
                return $"{counterPayload.DisplayName} ({counterPayload.Unit} / {counterPayload.Interval} sec)";
            }
            if (!string.IsNullOrEmpty(counterPayload.Unit))
            {
                return $"{counterPayload.DisplayName} ({counterPayload.Unit})";
            }
            return $"{counterPayload.DisplayName}";
        }
    }
}
