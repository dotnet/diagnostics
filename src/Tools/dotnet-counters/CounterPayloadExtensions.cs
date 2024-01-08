// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Monitoring.EventPipe;

namespace Microsoft.Diagnostics.Tools.Counters
{
    internal static class CounterPayloadExtensions
    {
        public static string GetDisplay(this ICounterPayload counterPayload)
        {
            if (!counterPayload.IsMeter)
            {
                string unit = counterPayload.Unit == "count" ? "Count" : counterPayload.Unit;
                if (counterPayload.CounterType == CounterType.Rate)
                {
                    return $"{counterPayload.DisplayName} ({unit} / {counterPayload.Series} sec)";
                }
                if (!string.IsNullOrEmpty(counterPayload.Unit))
                {
                    return $"{counterPayload.DisplayName} ({unit})";
                }
            }

            return $"{counterPayload.DisplayName}";
        }
    }
}
