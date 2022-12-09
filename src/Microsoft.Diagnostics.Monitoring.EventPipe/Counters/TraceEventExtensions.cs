// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class CounterConfiguration
    {
        public CounterConfiguration(CounterFilter filter)
        {
            CounterFilter = filter ?? throw new ArgumentNullException(nameof(filter));
        }

        public CounterFilter CounterFilter { get; }

        public string SessionId { get; set; } = null;

        public int MaxHistograms { get; set; } = 0;

        public int MaxTimeseries { get; set; } = 0;
    }

    internal static class TraceEventExtensions
    {
        public static bool TryGetCounterPayload(this TraceEvent traceEvent, CounterConfiguration counterConfiguration, out ICounterPayload payload)
        {
            payload = null;

            if ("EventCounters".Equals(traceEvent.EventName))
            {
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(traceEvent.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                //Make sure we are part of the requested series. If multiple clients request metrics, all of them get the metrics.
                string series = payloadFields["Series"].ToString();
                string counterName = payloadFields["Name"].ToString();

                string metadata = payloadFields["Metadata"].ToString();
                int seriesValue = GetInterval(series);
                //CONSIDER
                //Concurrent counter sessions do not each get a separate interval. Instead the payload
                //for _all_ the counters changes the Series to be the lowest specified interval, on a per provider basis.
                //Currently the CounterFilter will remove any data whose Series doesn't match the requested interval.
                if (!counterConfiguration.CounterFilter.IsIncluded(traceEvent.ProviderName, counterName, seriesValue))
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

                payload = new StandardCounterPayload(
                    traceEvent.TimeStamp,
                    traceEvent.ProviderName,
                    counterName, displayName,
                    displayUnits,
                    value,
                    counterType,
                    intervalSec,
                    seriesValue / 1000,
                    metadata));

                return true;
            }

            if (counterConfiguration.SessionId != null && MonitoringSourceConfiguration.SystemDiagnosticsMetricsProviderName.Equals(traceEvent.ProviderName))
            {
                if (traceEvent.EventName == "BeginInstrumentReporting")
                {
                    HandleBeginInstrumentReporting(traceEvent, counterConfiguration, out individualPayload);
                }
                if (traceEvent.EventName == "HistogramValuePublished")
                {
                    HandleHistogram(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "GaugeValuePublished")
                {
                    HandleGauge(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "CounterRateValuePublished")
                {
                    HandleCounterRate(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "UpDownCounterRateValuePublished")
                {
                    HandleUpDownCounterValue(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "TimeSeriesLimitReached")
                {
                    HandleTimeSeriesLimitReached(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "HistogramLimitReached")
                {
                    HandleHistogramLimitReached(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "Error")
                {
                    HandleError(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "ObservableInstrumentCallbackError")
                {
                    HandleObservableInstrumentCallbackError(traceEvent, counterConfiguration, out payload);
                }
                else if (traceEvent.EventName == "MultipleSessionsNotSupportedError")
                {
                    HandleMultipleSessionsNotSupportedError(traceEvent, counterConfiguration, out payload);
                }


                return payload != null;
            }

            return false;
        }

        private static void HandleGauge(TraceEvent obj, CounterConfiguration counterConfiguration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != counterConfiguration.SessionId)
            {
                return;
            }

            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string lastValueText = (string)obj.PayloadValue(6);

            if (!counterConfiguration.CounterFilter.IsIncluded(meterName, instrumentName))
            {
                return;
            }

            // the value might be an empty string indicating no measurement was provided this collection interval
            if (double.TryParse(lastValueText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double lastValue))
            {
                payload = new GaugePayload(meterName, instrumentName, null, unit, tags, lastValue, obj.TimeStamp);
            }
            else
            {
                // for observable instruments we assume the lack of data is meaningful and remove it from the UI
                // this happens when the Gauge callback function throws an exception.
                payload = new CounterEndedPayload(meterName, instrumentName, null, obj.TimeStamp);
                
            }
        }

        private static void HandleBeginInstrumentReporting(TraceEvent traceEvent, CounterConfiguration configuration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)traceEvent.PayloadValue(0);
            if (payloadSessionId != configuration.SessionId)
            {
                return;
            }

            string meterName = (string)traceEvent.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)traceEvent.PayloadValue(3);

            payload = new InstrumentationStartedPayload(meterName, instrumentName, traceEvent.TimeStamp);
        }

        private static void HandleCounterRate(TraceEvent traceEvent, CounterConfiguration counterConfiguration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)traceEvent.PayloadValue(0);

            if (payloadSessionId != counterConfiguration.SessionId)
            {
                return;
            }

            string meterName = (string)traceEvent.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)traceEvent.PayloadValue(3);
            string unit = (string)traceEvent.PayloadValue(4);
            string tags = (string)traceEvent.PayloadValue(5);
            string rateText = (string)traceEvent.PayloadValue(6);

            if (double.TryParse(rateText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            {
                return;
            }

            if (double.TryParse(valueText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                // UpDownCounter reports the value, not the rate - this is different than how Counter behaves.
                payload = new UpDownCounterPayload(meterName, instrumentName, null, unit, tags, value, traceEvent.TimeStamp);

            }
            else
            {
                // for observable instruments we assume the lack of data is meaningful and remove it from the UI
                // this happens when the ObservableUpDownCounter callback function throws an exception
                // or when the ObservableUpDownCounter doesn't include a measurement for a particular set of tag values.
                payload = new CounterEndedPayload(meterName, instrumentName, traceEvent.TimeStamp);
            }
        }

        private static void HandleUpDownCounterValue(TraceEvent traceEvent, CounterFilter filter, string sessionId, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)traceEvent.PayloadValue(0);

            if (payloadSessionId != sessionId || traceEvent.Version < 1) // Version 1 added the value field.
            {
                return;
            }

            string meterName = (string)traceEvent.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)traceEvent.PayloadValue(3);
            string unit = (string)traceEvent.PayloadValue(4);
            string tags = (string)traceEvent.PayloadValue(5);
            //string rateText = (string)traceEvent.PayloadValue(6); // Not currently using rate for UpDownCounters.
            string valueText = (string)traceEvent.PayloadValue(7);

            if (!filter.IsIncluded(meterName, instrumentName)) {
                return;
            }

            if (double.TryParse(valueText, NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) {
                // UpDownCounter reports the value, not the rate - this is different than how Counter behaves.
                payload = new UpDownCounterPayload(meterName, instrumentName, null, unit, tags, value, traceEvent.TimeStamp);

            }
            else {
                // for observable instruments we assume the lack of data is meaningful and remove it from the UI
                // this happens when the ObservableUpDownCounter callback function throws an exception
                // or when the ObservableUpDownCounter doesn't include a measurement for a particular set of tag values.
                payload = new CounterEndedPayload(meterName, instrumentName, traceEvent.TimeStamp);
            }
        }

        private static void HandleHistogram(TraceEvent obj, CounterFilter filter, string sessionId, out List<ICounterPayload> payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != configuration.SessionId)
            {
                return;
            }

            string meterName = (string)obj.PayloadValue(1);
            //string meterVersion = (string)obj.PayloadValue(2);
            string instrumentName = (string)obj.PayloadValue(3);
            string unit = (string)obj.PayloadValue(4);
            string tags = (string)obj.PayloadValue(5);
            string quantilesText = (string)obj.PayloadValue(6);

            if (!configuration.CounterFilter.IsIncluded(meterName, instrumentName))
            {
                return;
            }

            //Note quantiles can be empty.
            IList<Quantile> quantiles = ParseQuantiles(quantilesText);
            payload = new PercentilePayload(meterName, instrumentName, null, unit, tags, quantiles, obj.TimeStamp);
        }



        private static void HandleHistogramLimitReached(TraceEvent obj, CounterConfiguration configuration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != configuration.SessionId)
            {
                return;
            }

            string errorMessage = $"Warning: Histogram tracking limit ({configuration.MaxHistograms}) reached. Not all data is being shown." + Environment.NewLine +
                            "The limit can be changed but will use more memory in the target process.";

            payload = new ErrorPayload(errorMessage, obj.TimeStamp);
        }

        private static void HandleTimeSeriesLimitReached(TraceEvent obj, CounterConfiguration configuration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);

            if (payloadSessionId != configuration.SessionId)
            {
                return;
            }

            string errorMessage = $"Warning: Time series tracking limit ({configuration.MaxTimeseries}) reached. Not all data is being shown. The limit can be changed but will use more memory in the target process.";

            payload = new ErrorPayload(errorMessage, obj.TimeStamp);
        }

        private static void HandleError(TraceEvent obj, CounterConfiguration configuration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);
            string error = (string)obj.PayloadValue(1);
            if (configuration.SessionId != payloadSessionId)
            {
                return;
            }

            string errorMessage = "Error reported from target process:" + Environment.NewLine + error;

            payload = new ErrorPayload(errorMessage, obj.TimeStamp, ErrorType.TracingError);
        }

        private static void HandleMultipleSessionsNotSupportedError(TraceEvent obj, CounterConfiguration configuration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);
            if (payloadSessionId == configuration.SessionId)
            {
                // If our session is the one that is running then the error is not for us,
                // it is for some other session that came later
                return;
            }
            else
            {
                string errorMessage = "Error: Another metrics collection session is already in progress for the target process, perhaps from another tool? " + Environment.NewLine +
                "Concurrent sessions are not supported.";

                payload = new ErrorPayload(errorMessage, obj.TimeStamp, ErrorType.SessionStartupError);
            }
        }

        private static void HandleObservableInstrumentCallbackError(TraceEvent obj, CounterConfiguration configuration, out ICounterPayload payload)
        {
            payload = null;

            string payloadSessionId = (string)obj.PayloadValue(0);
            string error = (string)obj.PayloadValue(1);

            if (payloadSessionId != configuration.SessionId)
            {
                return;
            }

            string errorMessage = "Exception thrown from an observable instrument callback in the target process:" + Environment.NewLine +
                error;

            payload = new ErrorPayload(errorMessage, obj.TimeStamp);
        }

        private static IList<Quantile> ParseQuantiles(string quantileList)
        {
            string[] quantileParts = quantileList.Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<Quantile> quantiles = new();
            foreach (string quantile in quantileParts)
            {
                string[] keyValParts = quantile.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (keyValParts.Length != 2)
                {
                    continue;
                }
                if (!double.TryParse(keyValParts[0], NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double key))
                {
                    continue;
                }
                if (!double.TryParse(keyValParts[1], NumberStyles.Number | NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    continue;
                }
                quantiles.Add(new Quantile(key, val));
            }
            return quantiles;
        }

        private static int GetInterval(string series)
        {
            const string comparison = "Interval=";
            int interval = 0;
            if (series.StartsWith(comparison, StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(series.AsSpan(comparison.Length), out interval);
            }
            return interval;
        }
    }
}
