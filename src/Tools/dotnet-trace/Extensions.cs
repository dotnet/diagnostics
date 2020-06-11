﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class Extensions
    {
        public static string CLREventProviderName = "Microsoft-Windows-DotNETRuntime";

        private static EventLevel defaultEventLevel = EventLevel.Verbose;
        // Keep this in sync with runtime repo's clretwall.man
        private static Dictionary<string, long> CLREventKeywords = new Dictionary<string, long>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "gc", 0x1 },
            { "gchandle", 0x2 },
            { "fusion", 0x4 },
            { "loader", 0x8 },
            { "jit", 0x10 },
            { "ngen", 0x20 },
            { "startenumeration", 0x40 },
            { "endenumeration", 0x80 },
            { "security", 0x400 },
            { "appdomainresourcemanagement", 0x800 },
            { "jittracing", 0x1000 },
            { "interop", 0x2000 },
            { "contention", 0x4000 },
            { "exception", 0x8000 },
            { "threading", 0x10000 },
            { "jittedmethodiltonativemap", 0x20000 },
            { "overrideandsuppressngenevents", 0x40000 },
            { "type", 0x80000 },
            { "gcheapdump", 0x100000 },
            { "gcsampledobjectallcationhigh", 0x200000 },
            { "gcheapsurvivalandmovement", 0x400000 },
            { "gcheapcollect", 0x800000 },
            { "gcheapandtypenames", 0x1000000 },
            { "gcsampledobjectallcationlow", 0x2000000 },
            { "perftrack", 0x20000000 },
            { "stack", 0x40000000 },
            { "threadtransfer", 0x80000000 },
            { "debugger", 0x100000000 }
        };

        public static List<EventPipeProvider> ToProviders(string providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            return string.IsNullOrWhiteSpace(providers) ?
                new List<EventPipeProvider>() : providers.Split(',').Select(ToProvider).ToList();
        }

        public static EventPipeProvider ToCLREventPipeProvider(string clreventslist, string clreventlevel)
        {
            if (clreventslist == null || clreventslist.Length == 0)
            {
                return null;
            }

            var clrevents = clreventslist.Split("+");
            long clrEventsKeywordsMask = 0;
            for (var i = 0; i < clrevents.Length; i++)
            {
                if (CLREventKeywords.TryGetValue(clrevents[i], out var keyword))
                {
                    clrEventsKeywordsMask |= keyword;
                }
                else
                {
                    throw new ArgumentException($"{clrevents[i]} is not a valid CLR event keyword");
                }
            }

            EventLevel level = (EventLevel)4; // Default event level

            if (clreventlevel.Length != 0)
            {
                level = GetEventLevel(clreventlevel);
            }

            return new EventPipeProvider(CLREventProviderName, level, clrEventsKeywordsMask, null);
        }

        private static EventLevel GetEventLevel(string token)
        {
            if (Int32.TryParse(token, out int level) && level >= 0)
            {
                return level > (int)EventLevel.Verbose ? EventLevel.Verbose : (EventLevel)level;
            }

            else
            {
                switch (token.ToLower())
                {
                    case "critical":
                        return EventLevel.Critical;
                    case "error":
                        return EventLevel.Error;
                    case "informational":
                        return EventLevel.Informational;
                    case "logalways":
                        return EventLevel.LogAlways;
                    case "verbose":
                        return EventLevel.Verbose;
                    case "warning":
                        return EventLevel.Warning;
                    default:
                        throw new ArgumentException($"Unknown EventLevel: {token}");
                }
            }
        }

        private static EventPipeProvider ToProvider(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentNullException(nameof(provider));

            var tokens = provider.Split(new[] { ':' }, 4, StringSplitOptions.None); // Keep empty tokens;

            // Provider name
            string providerName = tokens.Length > 0 ? tokens[0] : null;

            // Check if the supplied provider is a GUID and not a name.
            if (Guid.TryParse(providerName, out _))
            {
                Console.WriteLine($"Warning: --provider argument {providerName} appears to be a GUID which is not supported by dotnet-trace. Providers need to be referenced by their textual name.");
            }

            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("Provider name was not specified.");

            // Keywords
            long keywords = tokens.Length > 1 && !string.IsNullOrWhiteSpace(tokens[1]) ?
                Convert.ToInt64(tokens[1], 16) : -1;

            // Level
            EventLevel eventLevel = tokens.Length > 2 && !string.IsNullOrWhiteSpace(tokens[2]) ?
                GetEventLevel(tokens[2]) : defaultEventLevel;

            // Event counters
            string filterData = tokens.Length > 3 ? tokens[3] : null;
            var argument = string.IsNullOrWhiteSpace(filterData) ? null : ParseArgumentString(filterData); 
            return new EventPipeProvider(providerName, eventLevel, keywords, argument);
        }

        private static Dictionary<string, string> ParseArgumentString(string argument)
        {
            if (argument == "")
            {
                return null;
            }
            var argumentDict = new Dictionary<string, string>();

            int keyStart = 0;
            int keyEnd = 0;
            int valStart = 0;
            int valEnd = 0;
            int curIdx = 0;
            bool inQuote = false;
            argument = Regex.Unescape(argument);
            foreach (var c in argument)
            {
                if (inQuote)
                {
                    if (c == '\"')
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    if (c == '=')
                    {
                        keyEnd = curIdx;
                        valStart = curIdx+1;
                    }
                    else if (c == ';')
                    {
                        valEnd = curIdx;
                        AddKeyValueToArgumentDict(argumentDict, argument, keyStart, keyEnd, valStart, valEnd);
                        keyStart = curIdx+1; // new key starts
                    }
                    else if (c == '\"')
                    {
                        inQuote = true;
                    }
                }
                curIdx += 1;
            }
            AddKeyValueToArgumentDict(argumentDict, argument, keyStart, keyEnd, valStart, valEnd);
            return argumentDict;
        }

        private static void AddKeyValueToArgumentDict(Dictionary<string, string> argumentDict, string argument, int keyStart, int keyEnd, int valStart, int valEnd)
        {
            string key = argument.Substring(keyStart, keyEnd - keyStart);
            string val = argument.Substring(valStart);
            if (val.StartsWith("\"") && val.EndsWith("\""))
            {
                val = val.Substring(1, val.Length - 2);
            }
            argumentDict.Add(key, val);
        }
    }
}
