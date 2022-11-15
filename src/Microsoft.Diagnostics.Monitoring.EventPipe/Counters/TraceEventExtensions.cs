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
        public static bool TryGetCounterPayload(this TraceEvent traceEvent, CounterFilter filter, out List<ICounterPayload> payload)
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

            if ("System.Diagnostics.Metrics".Equals(traceEvent.ProviderName))
            {
                ICounterPayload individualPayload = null;

                if (traceEvent.EventName == "BeginInstrumentReporting")
                {
                    //HandleBeginInstrumentReporting(traceEvent);
                }
                if (traceEvent.EventName == "HistogramValuePublished")
                {
                    HandleHistogram(traceEvent, out payload);
                }
                else if (traceEvent.EventName == "GaugeValuePublished")
                {
                    HandleGauge(traceEvent, out individualPayload);
                }
                else if (traceEvent.EventName == "CounterRateValuePublished")
                {
                    HandleCounterRate(traceEvent, out individualPayload);
                }
                else if (traceEvent.EventName == "TimeSeriesLimitReached")
                {
                    HandleTimeSeriesLimitReached(traceEvent, out individualPayload);
                }
                else if (traceEvent.EventName == "HistogramLimitReached")
                {
                    HandleHistogramLimitReached(traceEvent, out individualPayload);
                }
                else if (traceEvent.EventName == "Error")
                {
                    //HandleError(traceEvent);
                }
                else if (traceEvent.EventName == "ObservableInstrumentCallbackError")
                {
                    //HandleObservableInstrumentCallbackError(traceEvent);
                }
                else if (traceEvent.EventName == "MultipleSessionsNotSupportedError")
                {
                    //HandleMultipleSessionsNotSupportedError(traceEvent);
                }

                if (null != individualPayload)
                {
                    payload.Add(individualPayload);
                }

                return null != payload && payload.Any();
            }

            return false;
        }

        private static void HandleGauge(TraceEvent obj, out ICounterPayload payload)
        {
            payload = null;

            string sessionId = (string)obj.PayloadValue(0);
            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string lastValueText = (string)obj.PayloadValue(6);

            Dictionary<string, string> metadataDict = GetMetadata(tags);

            // the value might be an empty string indicating no measurement was provided this collection interval
            if (double.TryParse(lastValueText, out double lastValue))
            {
                payload = new GaugePayload(meterName, instrumentName, null, unit, metadataDict, lastValue, obj.TimeStamp);
            }
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

            Dictionary<string, string> metadataDict = GetMetadata(tags);

            if (double.TryParse(rateText, out double rate))
            {
                payload = new RatePayload(meterName, instrumentName, null, unit, metadataDict, rate, 10, traceEvent.TimeStamp); // NEED REAL VALUE FOR INTERVAL
            }
        }

        
        private static void HandleHistogram(TraceEvent obj, out List<ICounterPayload> payload)
        {
            payload = new List<ICounterPayload>();

            string sessionId = (string)obj.PayloadValue(0);
            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string quantilesText = (string)obj.PayloadValue(6);

            Console.WriteLine("MeterName: " + meterName);
            Console.WriteLine("InstrumentName: " + instrumentName);
            Console.WriteLine("Unit: " + unit);
            Console.WriteLine("Tags: " + tags);
            Console.WriteLine("Quantiles: " + quantilesText);


            KeyValuePair<double, double>[] quantiles = ParseQuantiles(quantilesText);
            foreach ((double key, double val) in quantiles)
            {
                Console.WriteLine("Key: " + key + " | Value: " + val);

                Dictionary<string, string> metadataDict = GetMetadata(tags);
                metadataDict.Add("quantile", key.ToString());
                payload.Add(new PercentilePayload(meterName, instrumentName, null, unit, metadataDict, val, obj.TimeStamp));
            }
        }

        private static void HandleHistogramLimitReached(TraceEvent obj, out ICounterPayload payload)
        {
            string sessionId = (string)obj.PayloadValue(0);

            string errorMessage = $"Warning: Histogram tracking limit reached. Not all data is being shown. The limit can be changed with maxHistograms but will use more memory in the target process.";

            payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, DateTime.Now, errorMessage); // NEED REAL VALUE FOR DATETIME
        }

        private static void HandleTimeSeriesLimitReached(TraceEvent obj, out ICounterPayload payload)
        {
            string sessionId = (string)obj.PayloadValue(0);

            string errorMessage = "Warning: Time series tracking limit reached. Not all data is being shown. The limit can be changed with maxTimeSeries but will use more memory in the target process.";

            payload = new ErrorPayload(string.Empty, string.Empty, string.Empty, string.Empty, new(), 0, DateTime.Now, errorMessage); // NEED REAL VALUE FOR DATETIME
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
