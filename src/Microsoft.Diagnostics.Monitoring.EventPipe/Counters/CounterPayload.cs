// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal abstract class CounterPayload : ICounterPayload
    {
        protected CounterPayload(DateTime timestamp,
            CachedCounterInfo counterInfo,
            string displayName,
            string unit,
            double value,
            CounterType counterType,
            float interval,
            int series,
            string metadata,
            EventType eventType)
        {
            Timestamp = timestamp;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            CounterType = counterType;
            CounterInfo = counterInfo;
            Interval = interval;
            Series = series;
            Metadata = metadata;
            EventType = eventType;
        }

        public string DisplayName { get; protected set; }

        public string Unit { get; }

        public double Value { get; }

        public DateTime Timestamp { get; }

        public float Interval { get; }

        public CounterType CounterType { get; }

        public CachedCounterInfo CounterInfo { get; }

        public string Metadata { get; }

        public EventType EventType { get; set; }

        public virtual bool IsMeter => false;

        public int Series { get; }
    }

    internal sealed class EventCounterPayload : CounterPayload
    {
        public EventCounterPayload(DateTime timestamp,
            string providerName,
            string name,
            string displayName,
            string unit,
            double value,
            CounterType counterType,
            float interval,
            int series,
            string metadata) : base(timestamp, new(providerName, name, null, null, null), displayName, unit, value, counterType, interval, series, metadata, EventType.Gauge)
        {
        }
    }

    internal abstract class MeterPayload : CounterPayload
    {
        protected MeterPayload(DateTime timestamp,
                    CachedCounterInfo counterInfo,
                    string displayName,
                    string unit,
                    double value,
                    CounterType counterType,
                    string metadata,
                    EventType eventType)
            : base(timestamp, counterInfo, displayName, unit, value, counterType, 0.0f, 0, metadata, eventType)
        {
        }

        public override bool IsMeter => true;
    }

    internal sealed class GaugePayload : MeterPayload
    {
        public GaugePayload(CachedCounterInfo counterInfo, string displayName, string displayUnits, string metadata, double value, DateTime timestamp) :
            base(timestamp, counterInfo, displayName, displayUnits, value, CounterType.Metric, metadata, EventType.Gauge)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterInfo.CounterName : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    internal class UpDownCounterPayload : MeterPayload
    {
        public UpDownCounterPayload(CachedCounterInfo counterInfo, string displayName, string displayUnits, string metadata, double value, DateTime timestamp) :
            base(timestamp, counterInfo, displayName, displayUnits, value, CounterType.Metric, metadata, EventType.UpDownCounter)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterInfo.CounterName : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    internal sealed class BeginInstrumentReportingPayload : MeterPayload
    {
        public BeginInstrumentReportingPayload(CachedCounterInfo counterInfo, DateTime timestamp)
            : base(timestamp, counterInfo, string.Empty, string.Empty, 0.0, CounterType.Metric, null, EventType.BeginInstrumentReporting)
        {
        }
    }

    internal sealed class CounterEndedPayload : MeterPayload
    {
        public CounterEndedPayload(CachedCounterInfo counterInfo, DateTime timestamp)
            : base(timestamp, counterInfo, string.Empty, string.Empty, 0.0, CounterType.Metric, null, EventType.CounterEnded)
        {
        }
    }

    internal sealed class RatePayload : MeterPayload
    {
        public RatePayload(CachedCounterInfo counterInfo, string displayName, string displayUnits, string metadata, double value, double intervalSecs, DateTime timestamp) :
            base(timestamp, counterInfo, displayName, displayUnits, value, CounterType.Rate, metadata, EventType.Rate)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterInfo.CounterName : displayName;
            string unitsName = string.IsNullOrEmpty(displayUnits) ? "Count" : displayUnits;
            string intervalName = intervalSecs.ToString() + " sec";
            DisplayName = $"{counterName} ({unitsName} / {intervalName})";
        }
    }

    internal record struct Quantile(double Percentage, double Value);

    internal sealed class PercentilePayload : MeterPayload
    {
        public PercentilePayload(CachedCounterInfo counterInfo, string displayName, string displayUnits, string metadata, double value, DateTime timestamp) :
            base(timestamp, counterInfo, displayName, displayUnits, value, CounterType.Metric, metadata, EventType.Histogram)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterInfo.CounterName : displayName;
            DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
        }
    }

    // Dotnet-monitor and dotnet-counters previously had incompatible PercentilePayload implementations before being unified -
    // Dotnet-monitor created a single payload that contained all of the quantiles to keep them together, whereas
    // dotnet-counters created a separate payload for each quantile (multiple payloads per TraceEvent).
    // AggregatePercentilePayload allows dotnet-monitor to construct a PercentilePayload for individual quantiles
    // like dotnet-counters, while still keeping the quantiles together as a unit.
    internal sealed class AggregatePercentilePayload : MeterPayload
    {
        public AggregatePercentilePayload(CachedCounterInfo counterInfo, string displayName, string displayUnits, string metadata, IEnumerable<Quantile> quantiles, DateTime timestamp) :
            base(timestamp, counterInfo, displayName, displayUnits, 0.0, CounterType.Metric, metadata, EventType.Histogram)
        {
            //string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            //DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
            Quantiles = quantiles.ToArray();
        }

        public Quantile[] Quantiles { get; }
    }

    internal sealed class ErrorPayload : MeterPayload
    {
        public ErrorPayload(string errorMessage, DateTime timestamp, EventType eventType)
            : base(timestamp, new(), string.Empty, string.Empty, 0.0, CounterType.Metric, null, eventType)
        {
            ErrorMessage = errorMessage;
        }

        public string ErrorMessage { get; }
    }

    internal enum EventType : int
    {
        Rate,
        Gauge,
        Histogram,
        UpDownCounter,
        BeginInstrumentReporting,
        CounterEnded,
        HistogramLimitError,
        TimeSeriesLimitError,
        ErrorTargetProcess,
        MultipleSessionsNotSupportedError,
        MultipleSessionsConfiguredIncorrectlyError,
        ObservableInstrumentCallbackError
    }

    internal static class CounterPayloadExtensions
    {
        public static string CombineTags(this ICounterPayload counterPayload)
        {
            StringBuilder builder = new();
            builder.Append(counterPayload.CounterInfo.MeterTags);

            if (!string.IsNullOrEmpty(counterPayload.CounterInfo.InstrumentTags))
            {
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }
                builder.Append(counterPayload.CounterInfo.InstrumentTags);
            }

            if (!string.IsNullOrEmpty(counterPayload.Metadata))
            {
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }
                builder.Append(counterPayload.Metadata);
            }

            return builder.ToString();
        }
    }

    internal static class EventTypeExtensions
    {
        public static bool IsValuePublishedEvent(this EventType eventType)
        {
            return eventType is EventType.Gauge
                || eventType is EventType.Rate
                || eventType is EventType.Histogram
                || eventType is EventType.UpDownCounter;
        }

        public static bool IsError(this EventType eventType)
        {
            return eventType is EventType.HistogramLimitError
                || eventType is EventType.TimeSeriesLimitError
                || eventType is EventType.ErrorTargetProcess
                || eventType is EventType.MultipleSessionsNotSupportedError
                || eventType is EventType.MultipleSessionsConfiguredIncorrectlyError
                || eventType is EventType.ObservableInstrumentCallbackError;
        }

        public static bool IsNonFatalError(this EventType eventType)
        {
            return IsError(eventType)
                && !IsTracingError(eventType)
                && !IsSessionStartupError(eventType);
        }

        public static bool IsTracingError(this EventType eventType)
        {
            return eventType is EventType.ErrorTargetProcess;
        }

        public static bool IsSessionStartupError(this EventType eventType)
        {
            return eventType is EventType.MultipleSessionsNotSupportedError
                || eventType is EventType.MultipleSessionsConfiguredIncorrectlyError;
        }
    }
}
