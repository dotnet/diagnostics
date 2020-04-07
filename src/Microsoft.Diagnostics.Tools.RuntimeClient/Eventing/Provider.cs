﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.Tracing;
using System.Text.RegularExpressions;

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
            FilterData = string.IsNullOrWhiteSpace(filterData) ? null : Regex.Unescape(filterData);
        }

        public ulong Keywords { get; }

        public EventLevel EventLevel { get; }

        public string Name { get; }

        public string FilterData { get; }

        public override string ToString() =>
            $"{Name}:0x{Keywords:X16}:{(uint)EventLevel}{(FilterData == null ? "" : $":{FilterData}")}";

        public string ToDisplayString() =>
            String.Format("{0, -40}", Name) + String.Format("0x{0, -18}", $"{Keywords:X16}") + String.Format("{0, -8}", EventLevel.ToString() + $"({(int)EventLevel})");
        
        public static bool operator ==(Provider left, Provider right)
        {
            return left.Name == right.Name &&
                left.Keywords == right.Keywords &&
                left.EventLevel == right.EventLevel &&
                left.FilterData == right.FilterData;
        }

        public static bool operator !=(Provider left, Provider right)
        {
            return !(left == right);
        }

        public override bool Equals(object obj)
        {
            
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }
            
            return this == (Provider)obj;
        }

        public override int GetHashCode()
        {
            int hash = 0;
            hash ^= this.Name.GetHashCode();
            hash ^= this.Keywords.GetHashCode();
            hash ^= this.EventLevel.GetHashCode();
            hash ^= this.FilterData.GetHashCode();
            return hash;
        }
    }
}
