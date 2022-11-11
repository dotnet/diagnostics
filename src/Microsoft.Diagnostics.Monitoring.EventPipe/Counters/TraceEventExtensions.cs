// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static class TraceEventExtensions
    {
        public static bool TryGetCounterPayload(this TraceEvent traceEvent, CounterFilter filter, out ICounterPayload payload)
        {
            payload = null;

            if ("EventCounters".Equals(traceEvent.EventName))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                //Make sure we are part of the requested series. If multiple clients request metrics, all of them get the metrics.
                string series = payloadFields["Series"].ToString();
                string counterName = payloadFields["Name"].ToString();

                //CONSIDER
                //Concurrent counter sessions do not each get a separate interval. Instead the payload
                //for _all_ the counters changes the Series to be the lowest specified interval, on a per provider basis.
                //Currently the CounterFilter will remove any data whose Series doesn't match the requested interval.
                if (!filter.IsIncluded(traceEvent.ProviderName, counterName, GetInterval(series)))
                {
                    return false;
                }

                float intervalSec = (float)payloadFields["IntervalSec"];
                string displayName = payloadFields["DisplayName"].ToString();
                string displayUnits = payloadFields["DisplayUnits"].ToString();
                double value = 0;
                CounterType counterType = CounterType.Metric;

                if (payloadFields["CounterType"].Equals("Mean"))
                {
                    value = (double)payloadFields["Mean"];
                }
                else if (payloadFields["CounterType"].Equals("Sum"))
                {
                    counterType = CounterType.Rate;
                    value = (double)payloadFields["Increment"];
                    if (string.IsNullOrEmpty(displayUnits))
                    {
                        displayUnits = "count";
                    }
                    //TODO Should we make these /sec like the dotnet-counters tool?
                }

                // Note that dimensional data such as pod and namespace are automatically added in prometheus and azure monitor scenarios.
                // We no longer added it here.
                payload = new CounterPayload(
                    traceEvent.TimeStamp,
                    traceEvent.ProviderName,
                    counterName, displayName,
                    displayUnits,
                    value,
                    counterType,
                    intervalSec);
                return true;
            }

            if ("System.Diagnostics.Metrics".Equals(traceEvent.ProviderName))
            {
                if (traceEvent.ProviderName == "System.Diagnostics.Metrics")
                {
                    if (traceEvent.EventName == "BeginInstrumentReporting")
                    {
                        HandleBeginInstrumentReporting(traceEvent);
                    }
                    if (traceEvent.EventName == "HistogramValuePublished")
                    {
                        HandleHistogram(traceEvent);
                    }
                    else if (traceEvent.EventName == "GaugeValuePublished")
                    {
                        HandleGauge(traceEvent);
                    }
                    else if (traceEvent.EventName == "CounterRateValuePublished")
                    {
                        HandleCounterRate(traceEvent, out payload);
                    }
                    else if (traceEvent.EventName == "TimeSeriesLimitReached")
                    {
                        HandleTimeSeriesLimitReached(traceEvent);
                    }
                    else if (traceEvent.EventName == "HistogramLimitReached")
                    {
                        HandleHistogramLimitReached(traceEvent);
                    }
                    else if (traceEvent.EventName == "Error")
                    {
                        HandleError(traceEvent);
                    }
                    else if (traceEvent.EventName == "ObservableInstrumentCallbackError")
                    {
                        HandleObservableInstrumentCallbackError(traceEvent);
                    }
                    else if (traceEvent.EventName == "MultipleSessionsNotSupportedError")
                    {
                        HandleMultipleSessionsNotSupportedError(traceEvent);
                    }
                }

                return payload != null;
            }

            return false;
        }

        private static void HandleCounterRate(TraceEvent traceEvent, out ICounterPayload payload)
        {
            payload = null;

            string sessionId = (string)traceEvent.PayloadValue(0);
            string meterName = (string)traceEvent.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)traceEvent.PayloadValue(3);
            string unit = (string)traceEvent.PayloadValue(4);
            string tags = (string)traceEvent.PayloadValue(5);
            string rateText = (string)traceEvent.PayloadValue(6);

            if (double.TryParse(rateText, out double rate))
            {
                payload = new RatePayload(meterName, instrumentName, null, unit, tags, rate, 10, traceEvent.TimeStamp); // NEED REAL VALUE FOR INTERVAL
            }
        }


        private static int GetInterval(string series)
        {
            const string comparison = "Interval=";
            int interval = 0;
            if (series.StartsWith(comparison, StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(series.Substring(comparison.Length), out interval);
            }
            return interval;
        }
    }
}
