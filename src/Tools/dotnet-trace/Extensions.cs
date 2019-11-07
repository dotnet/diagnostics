// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class Extensions
    {
        private static EventLevel defaultEventLevel = EventLevel.Verbose;

        public static List<EventPipeProvider> ToProviders(string providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            return string.IsNullOrWhiteSpace(providers) ?
                new List<EventPipeProvider>() : providers.Split(',').Select(ToProvider).ToList();
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

            // Check if the supplied provider is a GUID and not a name. if (Guid.TryParse(providerName, out _))
            { Console.WriteLine($"Warning: --provider argument {providerName} appears to be a GUID which is not supported by dotnet-trace. Providers need to be referenced by their textual name.");
                           }

            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("Provider name was not specified.");

            // Keywords
            long keywords = tokens.Length > 1 && !string.IsNullOrWhiteSpace(tokens[1]) ?
                Convert.ToInt64(tokens[1], 16) : long.MaxValue;

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
                        argumentDict.Add(argument.Substring(keyStart, keyEnd-keyStart), argument.Substring(valStart, valEnd-valStart));
                        keyStart = curIdx+1; // new key starts
                    }
                    else if (c == '\"')
                    {
                        inQuote = true;
                    }
                }
                curIdx += 1;
            }
            string key = argument.Substring(keyStart, keyEnd - keyStart);
            string val = argument.Substring(valStart);
            argumentDict.Add(key, val);
            return argumentDict;
        }
    }
}
