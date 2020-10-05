using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public static class CounterPayloadExtensions
    {
        public static string GetDisplay(this ICounterPayload counterPayload)
        {
            if (string.Equals(counterPayload.GetCounterType(), "Rate", StringComparison.OrdinalIgnoreCase))
            {
                return $"{counterPayload.GetDisplayName()} ({counterPayload.GetUnit()} / {counterPayload.GetInterval()} sec)";
            }
            if (!string.IsNullOrEmpty(counterPayload.GetUnit()))
            {
                return $"{counterPayload.GetDisplayName()} ({counterPayload.GetUnit()})";
            }
            return $"{counterPayload.GetDisplayName()}";
        }
    }
}
