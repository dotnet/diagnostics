// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public struct Provider
    {
        public Provider(
            string name,
            ulong keywords = ulong.MaxValue,
            EventLevel eventLevel = EventLevel.Verbose,
            string filterData = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            Name = name;
            Keywords = keywords;
            EventLevel = eventLevel;
            FilterData = string.IsNullOrWhiteSpace(filterData) ? null : filterData;
        }

        public static IReadOnlyCollection<Provider> ToProviders(string providers)
        {
            if (string.IsNullOrWhiteSpace(providers))
                throw new ArgumentNullException(nameof(providers));
            return providers.Split(',')
                .Select(ToProvider)
                .ToArray();
        }

        private static Provider ToProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentNullException(nameof(provider));

            var tokens = provider.Split(new[] { ':' }, 4, StringSplitOptions.None); // Keep empty tokens;

            // Provider name
            string providerName = tokens.Length > 0 ? tokens[0] : null;
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("Provider name was not specified.");

            // Keywords
            ulong keywords = tokens.Length > 1 ? Convert.ToUInt64(tokens[1], 16) : ulong.MaxValue;

            // Level
            EventLevel eventLevel = tokens.Length > 2 && uint.TryParse(tokens[2], out var level) ?
                (EventLevel)level : EventLevel.Verbose;

            // Event counters
            string filterData = tokens.Length > 3 ? tokens[3] : null;

            return new Provider(providerName, keywords, eventLevel, filterData);
        }

        public ulong Keywords { get; }

        public EventLevel EventLevel { get; }

        public string Name { get; }

        public string FilterData { get; }
    }
}
