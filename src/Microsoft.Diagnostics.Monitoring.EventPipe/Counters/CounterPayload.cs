// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal abstract class CounterPayload : ICounterPayload
    {
        private readonly string _DisplayUnits;

        protected CounterPayload(
            DateTime timestamp,
            CounterMetadata counterMetadata,
            string displayName,
            string displayUnits,
            double value,
            CounterType counterType,
            float interval,
            int series,
            string valueTags,
            EventType eventType)
        {
            Debug.Assert(counterMetadata != null);

            Timestamp = timestamp;
            DisplayName = displayName;
            _DisplayUnits = displayUnits;
            Value = value;
            CounterType = counterType;
            CounterMetadata = counterMetadata;
            Interval = interval;
            Series = series;
            ValueTags = valueTags;
            EventType = eventType;
        }

        public string DisplayName { get; protected set; }

        [Obsolete]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string Unit => CounterMetadata.CounterUnit;

        public string DisplayUnits => _DisplayUnits ?? CounterMetadata.CounterUnit;

        public double Value { get; }

        public DateTime Timestamp { get; }

        public float Interval { get; }

        public CounterType CounterType { get; }

        public CounterMetadata CounterMetadata { get; }

        public string ValueTags { get; }

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
            string displayUnits,
            double value,
            CounterType counterType,
            float interval,
            int series,
            string valueTags) : base(timestamp, new(providerName, name, displayUnits), displayName, displayUnits, value, counterType, interval, series, valueTags, EventType.Gauge)
        {
        }
    }

    internal abstract class MeterPayload : CounterPayload
    {
        protected MeterPayload(DateTime timestamp,
                    CounterMetadata counterMetadata,
                    string displayName,
                    string displayUnits,
                    double value,
                    CounterType counterType,
                    string valueTags,
                    EventType eventType)
            : base(timestamp, counterMetadata, displayName, displayUnits, value, counterType, 0.0f, 0, valueTags, eventType)
        {
        }

        public sealed override bool IsMeter => true;

        public virtual bool SupportsDelta { get; }
    }

    internal interface IRatePayload
    {
        double Rate { get; }
    }

    internal sealed class GaugePayload : MeterPayload
    {
        public GaugePayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, double value, DateTime timestamp) :
            base(timestamp, counterMetadata, displayName, displayUnits, value, CounterType.Metric, valueTags, EventType.Gauge)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterMetadata.CounterName : displayName;
            string unitsName = !string.IsNullOrEmpty(displayUnits)
                ? displayUnits
                : !string.IsNullOrEmpty(counterMetadata.CounterUnit)
                    ? counterMetadata.CounterUnit
                    : null;
            DisplayName = !string.IsNullOrEmpty(unitsName) ? $"{counterName} ({unitsName})" : counterName;
        }
    }

    internal sealed class UpDownCounterPayload : MeterPayload, IRatePayload
    {
        public UpDownCounterPayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, double value, DateTime timestamp)
            : this(counterMetadata, displayName, displayUnits, valueTags, rate: 0d, value, timestamp)
        {
        }

        public UpDownCounterPayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, double rate, double value, DateTime timestamp) :
            base(timestamp, counterMetadata, displayName, displayUnits, value, CounterType.Metric, valueTags, EventType.UpDownCounter)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterMetadata.CounterName : displayName;
            string unitsName = !string.IsNullOrEmpty(displayUnits)
                ? displayUnits
                : !string.IsNullOrEmpty(counterMetadata.CounterUnit)
                    ? counterMetadata.CounterUnit
                    : null;
            DisplayName = !string.IsNullOrEmpty(unitsName) ? $"{counterName} ({unitsName})" : counterName;
            Rate = rate;
        }

        public double Rate { get; }
    }

    internal sealed class BeginInstrumentReportingPayload : MeterPayload
    {
        public BeginInstrumentReportingPayload(CounterMetadata counterMetadata, DateTime timestamp)
            : base(timestamp, counterMetadata, string.Empty, string.Empty, 0.0, CounterType.Metric, null, EventType.BeginInstrumentReporting)
        {
        }
    }

    internal sealed class CounterEndedPayload : MeterPayload
    {
        public CounterEndedPayload(CounterMetadata counterMetadata, DateTime timestamp)
            : base(timestamp, counterMetadata, string.Empty, string.Empty, 0.0, CounterType.Metric, null, EventType.CounterEnded)
        {
        }
    }

    /// <summary>
    /// This gets generated for Counter instruments from Meters. This is used for pre-.NET 8 versions of MetricsEventSource that only reported rate and not absolute value,
    /// or for any tools that haven't opted into using RateAndValuePayload in the CounterConfiguration settings.
    /// </summary>
    internal sealed class RatePayload : MeterPayload, IRatePayload
    {
        public RatePayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, double rate, double intervalSecs, DateTime timestamp) :
            base(timestamp, counterMetadata, displayName, displayUnits, rate, CounterType.Rate, valueTags, EventType.Rate)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterMetadata.CounterName : displayName;
            string unitsName = !string.IsNullOrEmpty(displayUnits)
                ? displayUnits
                : !string.IsNullOrEmpty(counterMetadata.CounterUnit)
                    ? counterMetadata.CounterUnit
                    : "Count";
            string intervalName = intervalSecs.ToString() + " sec";
            DisplayName = $"{counterName} ({unitsName} / {intervalName})";
        }

        public double Rate => Value;

        public override bool SupportsDelta => true;
    }

    /// <summary>
    /// Starting in .NET 8, MetricsEventSource reports counters with both absolute value and rate. If enabled in the CounterConfiguration and the new value field is present
    /// then this payload will be created rather than the older RatePayload. Unlike RatePayload, this one treats the absolute value as the primary statistic.
    /// </summary>
    internal sealed class CounterRateAndValuePayload : MeterPayload, IRatePayload
    {
        public CounterRateAndValuePayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, double rate, double value, DateTime timestamp) :
            base(timestamp, counterMetadata, displayName, displayUnits, value, CounterType.Metric, valueTags, EventType.Rate)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterMetadata.CounterName : displayName;
            string unitsName = !string.IsNullOrEmpty(displayUnits)
                ? displayUnits
                : !string.IsNullOrEmpty(counterMetadata.CounterUnit)
                    ? counterMetadata.CounterUnit
                    : "Count";
            DisplayName = $"{counterName} ({unitsName})";
            Rate = rate;
        }

        public double Rate { get; }

        public override bool SupportsDelta => true;
    }

    internal record struct Quantile(double Percentage, double Value);

    internal sealed class PercentilePayload : MeterPayload
    {
        public PercentilePayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, double value, DateTime timestamp) :
            base(timestamp, counterMetadata, displayName, displayUnits, value, CounterType.Metric, valueTags, EventType.Histogram)
        {
            // In case these properties are not provided, set them to appropriate values.
            string counterName = string.IsNullOrEmpty(displayName) ? counterMetadata.CounterName : displayName;
            string unitsName = !string.IsNullOrEmpty(displayUnits)
                ? displayUnits
                : !string.IsNullOrEmpty(counterMetadata.CounterUnit)
                    ? counterMetadata.CounterUnit
                    : null;
            DisplayName = !string.IsNullOrEmpty(unitsName) ? $"{counterName} ({unitsName})" : counterName;
        }
    }

    // Dotnet-monitor and dotnet-counters previously had incompatible PercentilePayload implementations before being unified -
    // Dotnet-monitor created a single payload that contained all of the quantiles to keep them together, whereas
    // dotnet-counters created a separate payload for each quantile (multiple payloads per TraceEvent).
    // AggregatePercentilePayload allows dotnet-monitor to construct a PercentilePayload for individual quantiles
    // like dotnet-counters, while still keeping the quantiles together as a unit.
    internal sealed class AggregatePercentilePayload : MeterPayload
    {
        public AggregatePercentilePayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, IEnumerable<Quantile> quantiles, DateTime timestamp)
            : this(counterMetadata, displayName, displayUnits, valueTags, count: 0, sum: 0, quantiles, timestamp)
        {
        }

        public AggregatePercentilePayload(CounterMetadata counterMetadata, string displayName, string displayUnits, string valueTags, int count, double sum, IEnumerable<Quantile> quantiles, DateTime timestamp) :
            base(timestamp, counterMetadata, displayName, displayUnits, 0.0, CounterType.Metric, valueTags, EventType.Histogram)
        {
            Count = count;
            Sum = sum;
            //string counterName = string.IsNullOrEmpty(displayName) ? name : displayName;
            //DisplayName = !string.IsNullOrEmpty(displayUnits) ? $"{counterName} ({displayUnits})" : counterName;
            Quantiles = quantiles.ToArray();
        }

        public int Count { get; }

        public double Sum { get; }

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
            builder.Append(counterPayload.CounterMetadata.MeterTags);

            if (!string.IsNullOrEmpty(counterPayload.CounterMetadata.InstrumentTags))
            {
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }
                builder.Append(counterPayload.CounterMetadata.InstrumentTags);
            }

            if (!string.IsNullOrEmpty(counterPayload.ValueTags))
            {
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }
                builder.Append(counterPayload.ValueTags);
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
