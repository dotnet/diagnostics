// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.Tracing;

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

        public ulong Keywords { get; }

        public EventLevel EventLevel { get; }

        public string Name { get; }

        public string FilterData { get; }

        public override string ToString() =>
            $"{Name}:0x{Keywords:X16}:{(uint)EventLevel}{(FilterData == null ? "" : $":{FilterData}")}";
    }
}
