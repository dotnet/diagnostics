// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class CounterPayload : ICounterPayload
    {
#if NETSTANDARD
        private static readonly IReadOnlyDictionary<string, string> Empty = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0));
#else
        private static readonly IReadOnlyDictionary<string, string> Empty = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;
#endif

        public CounterPayload(DateTime timestamp,
            string provider,
            string name,
            string displayName,
            string unit,
            double value,
            CounterType counterType,
            float interval,
            Dictionary<string, string> metadata)
        {
            Timestamp = timestamp;
            Name = name;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            CounterType = counterType;
            Provider = provider;
            Interval = interval;
            Metadata = metadata ?? Empty;
        }

        public string Namespace { get; }

        public string Name { get; }

        public string DisplayName { get; }

        public string Unit { get; }

        public double Value { get; }

        public DateTime Timestamp { get; }

        public float Interval { get; }

        public CounterType CounterType { get; }

        public string Provider { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}