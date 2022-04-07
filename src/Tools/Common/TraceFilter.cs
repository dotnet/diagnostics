// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Tools.Common
{
    internal class PredicateBuilder
    {
        private class ProviderPredicate
        {
            public string ProviderName { get; private set; }
            private string eventNameInclude = "";
            private long keywordInclude = 0;
            private HashSet<int> eventIdInclude = new();

            public ProviderPredicate(string providerName)
            {
                ProviderName = providerName;
            }

            public void IncludeEventName(string pattern)
            {
                string sanitizedPattern = pattern.Replace("*", ".*");
                eventNameInclude = $"{sanitizedPattern.ToLowerInvariant()}{(string.IsNullOrEmpty(eventNameInclude) ? "" : $"|{eventNameInclude}")}";
            }

            public void IncludeKeyword(long keyword)
            {
                keywordInclude |= keyword;
            }

            public void IncludeEventId(int eventId)
            {
                eventIdInclude.Add(eventId);
            }

            public bool CheckPredicate(TraceEvent data)
            {
                bool ret = true;
                if (!string.IsNullOrEmpty(eventNameInclude))
                {
                    ret &= Regex.IsMatch(data.EventName.ToLowerInvariant(), eventNameInclude);
                }

                if (keywordInclude != 0)
                {
                    ret &= ((long)data.Keywords & keywordInclude) != 0;
                }

                if (eventIdInclude.Count != 0)
                {
                    ret &= eventIdInclude.Contains((int)data.ID);
                }

                return ret;
            }
        }

        private readonly Dictionary<string, ProviderPredicate> providerPredicates = new();
        private string providerNameIncludePattern;
        private string providerNameExcludePattern;

        public PredicateBuilder() { }

        public PredicateBuilder AddProviderPattern(string pattern, bool exclude = false) => exclude switch
        {
            true => ExcludeProviderPattern(pattern),
            false => IncludeProviderPattern(pattern)
        };

        public PredicateBuilder ExcludeProviderPattern(string pattern)
        {
            string sanitizedPattern = pattern.Replace("*", ".*");
            SimpleLogger.Log.WriteLine($"Adding exclude pattern: '{sanitizedPattern}'");
            providerNameExcludePattern = $"{sanitizedPattern.ToLowerInvariant()}{(string.IsNullOrEmpty(providerNameExcludePattern) ? "" : $"|{providerNameExcludePattern}")}";
            return this;
        }

        public PredicateBuilder IncludeProviderPattern(string pattern)
        {
            string sanitizedPattern = pattern.Replace("*", ".*");
            SimpleLogger.Log.WriteLine($"Adding include pattern: '{sanitizedPattern}'");
            providerNameIncludePattern = $"{sanitizedPattern.ToLowerInvariant()}{(string.IsNullOrEmpty(providerNameIncludePattern) ? "" : $"|{providerNameIncludePattern}")}";
            return this;
        }

        public PredicateBuilder AddProviderFilter(string providerName, string eventNamePattern)
        {
            string key = providerName.ToLowerInvariant();
            if (!providerPredicates.ContainsKey(key))
                providerPredicates[key] = new ProviderPredicate(key);

            SimpleLogger.Log.WriteLine($"Adding event name filter: Provider={providerName}, pattern={eventNamePattern}");

            providerPredicates[key].IncludeEventName(eventNamePattern);
            return this;
        }

        public PredicateBuilder AddProviderFilter(string providerName, int eventId)
        {
            string key = providerName.ToLowerInvariant();
            if (!providerPredicates.ContainsKey(key))
                providerPredicates[key] = new ProviderPredicate(key);

            SimpleLogger.Log.WriteLine($"Adding event id filter: Provider={providerName}, id={eventId}");

            providerPredicates[key].IncludeEventId(eventId);
            return this;
        }

        public PredicateBuilder AddProviderFilter(string providerName, long keyword)
        {
            string key = providerName.ToLowerInvariant();
            if (!providerPredicates.ContainsKey(key))
                providerPredicates[key] = new ProviderPredicate(key);

            SimpleLogger.Log.WriteLine($"Adding event keyword filter: Provider={providerName}, keyword={keyword:X}");

            providerPredicates[key].IncludeKeyword(keyword);
            return this;
        }

        public Func<TraceEvent, bool> Build()
        {
            Func<TraceEvent, bool> predicate = (_) => true;

            if (string.IsNullOrEmpty(providerNameIncludePattern) && string.IsNullOrEmpty(providerNameExcludePattern) && providerPredicates.Count == 0)
                return predicate;

            // order of checking:
            // 1. exclude regex
            // 2. include regex
            // 3. subfilters
            // effectively: exclude & include & (subfilters OR'd together)
            predicate = (TraceEvent data) =>
            {
                bool ret = true;
                if (!string.IsNullOrEmpty(providerNameExcludePattern))
                    ret &= !Regex.IsMatch(data.ProviderName.ToLowerInvariant(), providerNameExcludePattern);

                if (!string.IsNullOrEmpty(providerNameIncludePattern))
                    ret &= Regex.IsMatch(data.ProviderName.ToLowerInvariant(), providerNameIncludePattern);

                if (providerPredicates.Count != 0)
                {
                    string key = data.ProviderName.ToLowerInvariant();
                    if (ret &= providerPredicates.TryGetValue(key, out ProviderPredicate providerPredicate))
                    {
                        ret &= providerPredicate.CheckPredicate(data);
                    }
                }

                return ret;
            };


            SimpleLogger.Log.WriteLine($"Include regex: {providerNameIncludePattern}");
            SimpleLogger.Log.WriteLine($"Exclude regex: {providerNameExcludePattern}");

            return predicate;
        }

        public static Func<TraceEvent, bool> ParseFilter(string filter)
        {
            // Filter ::= 𝞮 | <NameFilter> | <Name>:<Subfilter> | -<Filter> | <Filter>;<Filter>
            // Subfilter ::= id=<Number> | name=<NameFilter> | keyword=<Number>
            // NameFilter ::= [a-zA-Z0-9\*]+
            // Name ::= [a-zA-Z0-9]+
            // Number ::= [1-9]+[0-9]* | 0x[0-9a-fA-F]* | 0b[01]*

            PredicateBuilder builder = new();

            string[] filters = filter.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (string f in filters)
            {
                string filterSection = f;
                bool exclude = false;
                if (filterSection.StartsWith('-'))
                {
                    exclude = true;
                    filterSection = filterSection.TrimStart('-');
                }

                string[] filterParts = filterSection.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (filterParts[0].Contains('*') || filterParts.Length == 1)
                {
                    builder.AddProviderPattern(filterParts[0], exclude);
                }
                else if (filterParts.Length == 2)
                {
                    // Regular name
                    string[] subfilterParts = filterParts[1].Split('=', StringSplitOptions.RemoveEmptyEntries);
                    Debug.Assert(subfilterParts.Length == 2);
                    if (subfilterParts.Length != 2)
                    {
                        SimpleLogger.Log.WriteLine($"FILTER ERROR :: Invalid Subfilter '{filterParts[1]}'");
                        continue;
                    }

                    switch (subfilterParts[0].ToLowerInvariant())
                    {
                        case "id":
                            if (int.TryParse(subfilterParts[1], out int id))
                                builder.AddProviderFilter(filterParts[0], id);
                            else
                                SimpleLogger.Log.WriteLine($"FILTER ERROR :: Failed to parse int from '{subfilterParts[1]}'");
                            break;
                        case "name":
                            builder.AddProviderFilter(filterParts[0], subfilterParts[1]);
                            break;
                        case "keyword":
                            if (TryParseLong(subfilterParts[1], out long keyword))
                                builder.AddProviderFilter(filterParts[0], keyword);
                            else
                                SimpleLogger.Log.WriteLine($"FILTER ERROR :: Failed to parse long from '{subfilterParts[1]}'");
                            break;
                        default:
                            SimpleLogger.Log.WriteLine($"FILTER ERROR :: Unknown subfilter '{subfilterParts[0]}'");
                            break;
                    }
                }
                else
                {
                    SimpleLogger.Log.WriteLine($"FILTER ERROR :: Invalid Filter '{f}");
                }
            }

            return builder.Build();
        }

        private static bool TryParseLong(string str, out long result)
        {
            bool ret = false;
            result = 0;
            if (str.StartsWith("0x") || str.StartsWith("0X"))
            {
                ret = long.TryParse(str, System.Globalization.NumberStyles.AllowHexSpecifier | System.Globalization.NumberStyles.HexNumber, null, out long val);
                result = val;
            }
            else if (str.StartsWith("0b") || str.StartsWith("0B"))
            {
                // Parse Binary
                int shift = 0;
                for (int i = str.Length - 1; i > 1; i--)
                {
                    if (str[i] == '1')
                        result |= 1L << shift;
                    else if (str[i] != '0')
                        return false;
                    shift++;
                }
                ret = true;
            }
            else
            {
                ret = long.TryParse(str, out long val);
                result = val;
            }
            return ret;
        }
    }
}