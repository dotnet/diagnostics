// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static class CounterPayloadExtensions
    {
        internal enum DisplayRenderingMode
        {
            Default,
            DotnetCounters
        }

        public static string GetDisplay(this ICounterPayload counterPayload, DisplayRenderingMode displayRenderingMode = DisplayRenderingMode.Default)
        {
            if (!counterPayload.IsMeter)
            {
                if (counterPayload.CounterType == CounterType.Rate)
                {
                    return $"{counterPayload.DisplayName} ({GetUnit(counterPayload.Unit, displayRenderingMode)} / {GetInterval(counterPayload, displayRenderingMode)} sec)";
                }
                if (!string.IsNullOrEmpty(counterPayload.Unit))
                {
                    return $"{counterPayload.DisplayName} ({counterPayload.Unit})";
                }
            }

            return $"{counterPayload.DisplayName}";
        }

        private static string GetUnit(string unit, DisplayRenderingMode displayRenderingMode)
        {
            if (displayRenderingMode == DisplayRenderingMode.DotnetCounters && string.Equals(unit, "count", StringComparison.OrdinalIgnoreCase))
            {
                return "Count";
            }
            return unit;
        }

        private static string GetInterval(ICounterPayload payload, DisplayRenderingMode displayRenderingMode) =>
            displayRenderingMode == DisplayRenderingMode.DotnetCounters ? payload.Series.ToString() : payload.Interval.ToString();
    }
}
