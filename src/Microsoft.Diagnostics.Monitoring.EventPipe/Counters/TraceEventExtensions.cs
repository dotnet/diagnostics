// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static class TraceEventExtensions
    {
        public static bool TryGetCounterPayload(this TraceEvent traceEvent, CounterFilter filter, string sessionId, out List<ICounterPayload> payload)
        {
            payload = new List<ICounterPayload>();

            if ("EventCounters".Equals(traceEvent.EventName))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                //Make sure we are part of the requested series. If multiple clients request metrics, all of them get the metrics.
                string series = payloadFields["Series"].ToString();
                string counterName = payloadFields["Name"].ToString();

                Dictionary<string, string> metadataDict = GetMetadata(payloadFields["Metadata"].ToString());

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

                payload.Add(new CounterPayload(
                    traceEvent.TimeStamp,
                    traceEvent.ProviderName,
                    counterName, displayName,
                    displayUnits,
                    value,
                    counterType,
                    intervalSec,
                    metadataDict));

                return true;
            }

            if (sessionId != null && "System.Diagnostics.Metrics".Equals(traceEvent.ProviderName))
            {
                
                ICounterPayload individualPayload = null;

                if (traceEvent.EventName == "BeginInstrumentReporting")
                {
                    // Do we want to log something for this?
                    //HandleBeginInstrumentReporting(traceEvent);
                }
                if (traceEvent.EventName == "HistogramValuePublished")
                {
                    HandleHistogram(traceEvent, filter, sessionId, out payload);
                }
                else if (traceEvent.EventName == "GaugeValuePublished")
                {
                    HandleGauge(traceEvent, filter, sessionId, out individualPayload);
                }
                else if (traceEvent.EventName == "CounterRateValuePublished")
                {
                    HandleCounterRate(traceEvent, filter, sessionId, out individualPayload);
                }
                else if (traceEvent.EventName == "TimeSeriesLimitReached")
                {
                    HandleTimeSeriesLimitReached(traceEvent, sessionId, out individualPayload);
                }
                else if (traceEvent.EventName == "HistogramLimitReached")
                {
                    HandleHistogramLimitReached(traceEvent, sessionId, out individualPayload);
                }
                else if (traceEvent.EventName == "Error")
                {
                    HandleError(traceEvent, sessionId, out individualPayload);
                }
                else if (traceEvent.EventName == "ObservableInstrumentCallbackError")
                {
                    HandleObservableInstrumentCallbackError(traceEvent, sessionId, out individualPayload);
                }
                else if (traceEvent.EventName == "MultipleSessionsNotSupportedError")
                {
                    HandleMultipleSessionsNotSupportedError(traceEvent, sessionId, out individualPayload);
                }

                if (null != individualPayload)
                {
                    payload.Add(individualPayload);
                }

                return null != payload && payload.Any();
            }

            return false;
        }

        public static bool TryGetCounterPayload(this TraceEvent traceEvent, CounterFilter filter, out List<ICounterPayload> payload)
        {
            return TryGetCounterPayload(traceEvent, filter, null, out payload);
        }

        private static void HandleGauge(TraceEvent obj, CounterFilter filter, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != sessionId)
            {
                return;
            }

            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string lastValueText = (string)obj.PayloadValue(6);

            Dictionary<string, string> metadataDict = GetMetadata(tags);

            if (!filter.IsIncluded(meterName, instrumentName))
            {
                return;
            }

            // the value might be an empty string indicating no measurement was provided this collection interval
            if (double.TryParse(lastValueText, out double lastValue))
            {
                payload = new GaugePayload(meterName, instrumentName, null, unit, metadataDict, lastValue, obj.TimeStamp);
            }
        }

        private static void HandleCounterRate(TraceEvent traceEvent, CounterFilter filter, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)traceEvent.PayloadValue(0);

            if (payloadSessionId != sessionId)
            {
                return;
            }

            string meterName = (string)traceEvent.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)traceEvent.PayloadValue(3);
            string unit = (string)traceEvent.PayloadValue(4);
            string tags = (string)traceEvent.PayloadValue(5);
            string rateText = (string)traceEvent.PayloadValue(6);

            Dictionary<string, string> metadataDict = GetMetadata(tags);

            if (double.TryParse(rateText, out double rate))
            {
                payload = new RatePayload(meterName, instrumentName, null, unit, metadataDict, rate, 10, traceEvent.TimeStamp);
            }
        }

        private static void HandleHistogram(TraceEvent obj, CounterFilter filter, string sessionId, out List<ICounterPayload> payload)
        {
            payload = new List<ICounterPayload>();

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != sessionId)
            {
                return;
            }

            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string quantilesText = (string)obj.PayloadValue(6);

            if (!filter.IsIncluded(meterName, instrumentName))
            {
                return;
            }

            KeyValuePair<double, double>[] quantiles = ParseQuantiles(quantilesText);
            foreach ((double key, double val) in quantiles)
            {
                Dictionary<string, string> metadataDict = GetMetadata(tags);
                metadataDict.Add("quantile", key.ToString());
                payload.Add(new PercentilePayload(meterName, instrumentName, null, unit, metadataDict, val, obj.TimeStamp));
            }
        }

        private static void HandleHistogramLimitReached(TraceEvent obj, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != sessionId)
            {
                return;
            }

            string errorMessage = $"Warning: Histogram tracking limit reached. Not all data is being shown. The limit can be changed with maxHistograms but will use more memory in the target process.";

            payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, obj.TimeStamp, errorMessage);
        }

        private static void HandleTimeSeriesLimitReached(TraceEvent obj, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != sessionId)
            {
                return;
            }

            string errorMessage = "Warning: Time series tracking limit reached. Not all data is being shown. The limit can be changed with maxTimeSeries but will use more memory in the target process.";

            payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, obj.TimeStamp, errorMessage);
        }

        private static void HandleError(TraceEvent obj, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);
            string error = (string)obj.PayloadValue(1);
            if (sessionId != payloadSessionId)
            {
                return;
            }

            string errorMessage = "Error reported from target process:" + Environment.NewLine + error;

            payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, obj.TimeStamp, errorMessage);
        }

        private static void HandleMultipleSessionsNotSupportedError(TraceEvent obj, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);
            if (payloadSessionId == sessionId)
            {
                // If our session is the one that is running then the error is not for us,
                // it is for some other session that came later
                return;
            }
            else
            {
                string errorMessage = "Error: Another metrics collection session is already in progress for the target process, perhaps from another tool? " + Environment.NewLine +
                "Concurrent sessions are not supported.";

                payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, obj.TimeStamp, errorMessage);
            }
        }

        private static void HandleObservableInstrumentCallbackError(TraceEvent obj, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);
            string error = (string)obj.PayloadValue(1);

            if (payloadSessionId != sessionId)
            {
                return;
            }

            string errorMessage = "Exception thrown from an observable instrument callback in the target process:" + Environment.NewLine +
                error;

            payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, obj.TimeStamp, errorMessage);
        }

        public static Dictionary<string, string> GetMetadata(string metadataPayload)
        {
            var metadataDict = new Dictionary<string, string>();

            ReadOnlySpan<char> metadata = metadataPayload;

            while (!metadata.IsEmpty)
            {
                int commaIndex = metadata.IndexOf(',');

                ReadOnlySpan<char> kvPair;

                if (commaIndex < 0)
                {
                    kvPair = metadata;
                    metadata = default;
                }
                else
                {
                    kvPair = metadata[..commaIndex];
                    metadata = metadata.Slice(commaIndex + 1);
                }

                int colonIndex = kvPair.IndexOf(':');
                if (colonIndex < 0)
                {
                    metadataDict.Clear();
                    break;
                }

                string metadataKey = kvPair[..colonIndex].ToString();
                string metadataValue = kvPair.Slice(colonIndex + 1).ToString();
                metadataDict[metadataKey] = metadataValue;
            }

            return metadataDict;
        }

        private static KeyValuePair<double, double>[] ParseQuantiles(string quantileList)
        {
            string[] quantileParts = quantileList.Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<KeyValuePair<double, double>> quantiles = new List<KeyValuePair<double, double>>();
            foreach (string quantile in quantileParts)
            {
                string[] keyValParts = quantile.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (keyValParts.Length != 2)
                {
                    continue;
                }
                if (!double.TryParse(keyValParts[0], out double key))
                {
                    continue;
                }
                if (!double.TryParse(keyValParts[1], out double val))
                {
                    continue;
                }
                quantiles.Add(new KeyValuePair<double, double>(key, val));
            }
            return quantiles.ToArray();
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
