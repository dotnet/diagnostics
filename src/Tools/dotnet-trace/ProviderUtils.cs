// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools;
using Microsoft.Diagnostics.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ProviderUtils
    {
        public static string CLREventProviderName = "Microsoft-Windows-DotNETRuntime";

        private const EventLevel defaultEventLevel = EventLevel.Informational;
        private const long defaultKeywords = 0;

        // Keep this in sync with runtime repo's clretwall.man
        private static Dictionary<string, long> CLREventKeywords = new(StringComparer.InvariantCultureIgnoreCase)
        {
            { "gc", 0x1 },
            { "gchandle", 0x2 },
            { "fusion", 0x4 },
            { "assemblyloader", 0x4 },
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
            { "gcsampledobjectallocationhigh", 0x200000 },
            { "gcheapsurvivalandmovement", 0x400000 },
            { "gcheapcollect", 0x800000 },
            { "managedheadcollect", 0x800000 },
            { "gcheapandtypenames", 0x1000000 },
            { "gcsampledobjectallocationlow", 0x2000000 },
            { "perftrack", 0x20000000 },
            { "stack", 0x40000000 },
            { "threadtransfer", 0x80000000 },
            { "debugger", 0x100000000 },
            { "monitoring", 0x200000000 },
            { "codesymbols", 0x400000000 },
            { "eventsource", 0x800000000 },
            { "compilation", 0x1000000000 },
            { "compilationdiagnostic", 0x2000000000 },
            { "methoddiagnostic", 0x4000000000 },
            { "typediagnostic", 0x8000000000 },
            { "jitinstrumentationdata", 0x10000000000 },
            { "profiler", 0x20000000000 },
            { "waithandle", 0x40000000000 },
            { "allocationsampling", 0x80000000000 },
        };

        private enum ProviderSource
        {
            ProvidersArg = 1,
            CLREventsArg = 2,
            ProfileArg = 4,
        }

        public static List<EventPipeProvider> ComputeProviderConfig(string[] providersArg, string clreventsArg, string clreventlevel, string[] profiles, bool shouldPrintProviders = false, string verbExclusivity = null, IConsole console = null)
        {
            console ??= new DefaultConsole();
            Dictionary<string, EventPipeProvider> merged = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> providerSources = new(StringComparer.OrdinalIgnoreCase);

            foreach (string providerArg in providersArg)
            {
                EventPipeProvider provider = ToProvider(providerArg, console);
                if (!merged.TryGetValue(provider.Name, out EventPipeProvider existing))
                {
                    merged[provider.Name] = provider;
                    providerSources[provider.Name] = (int)ProviderSource.ProvidersArg;
                }
                else
                {
                    merged[provider.Name] = MergeProviderConfigs(existing, provider);
                }
            }

            foreach (string profile in profiles)
            {
                Profile traceProfile = ListProfilesCommandHandler.TraceProfiles
                    .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));

                if (traceProfile == null)
                {
                    throw new DiagnosticToolException($"Invalid profile name: {profile}");
                }

                if (!string.IsNullOrEmpty(verbExclusivity) &&
                    !string.IsNullOrEmpty(traceProfile.VerbExclusivity) &&
                    !string.Equals(traceProfile.VerbExclusivity, verbExclusivity, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DiagnosticToolException($"The specified profile '{traceProfile.Name}' does not apply to `dotnet-trace {verbExclusivity}`.");
                }

                IEnumerable<EventPipeProvider> profileProviders = traceProfile.Providers;
                foreach (EventPipeProvider provider in profileProviders)
                {
                    if (merged.TryAdd(provider.Name, provider))
                    {
                        providerSources[provider.Name] = (int)ProviderSource.ProfileArg;
                    }
                    // Prefer providers set through --providers over implicit profile configuration
                }
            }

            if (!string.IsNullOrEmpty(clreventsArg))
            {
                EventPipeProvider provider = ToCLREventPipeProvider(clreventsArg, clreventlevel);
                if (provider is not null)
                {
                    if (!merged.TryGetValue(provider.Name, out EventPipeProvider existing))
                    {
                        merged[provider.Name] = provider;
                        providerSources[provider.Name] = (int)ProviderSource.CLREventsArg;
                    }
                    else if (shouldPrintProviders)
                    {
                        console.WriteLine($"Warning: The CLR provider was already specified through --providers or --profile. Ignoring --clrevents.");
                    }
                }
            }

            List<EventPipeProvider> unifiedProviders = merged.Values.ToList();
            if (shouldPrintProviders)
            {
                PrintProviders(unifiedProviders, providerSources, console);
            }

            return unifiedProviders;
        }

        private static EventPipeProvider MergeProviderConfigs(EventPipeProvider providerConfigA, EventPipeProvider providerConfigB)
        {
            Debug.Assert(string.Equals(providerConfigA.Name, providerConfigB.Name, StringComparison.OrdinalIgnoreCase));

            EventLevel level = (providerConfigA.EventLevel == EventLevel.LogAlways || providerConfigB.EventLevel == EventLevel.LogAlways) ?
                                EventLevel.LogAlways :
                                (providerConfigA.EventLevel > providerConfigB.EventLevel ? providerConfigA.EventLevel : providerConfigB.EventLevel);

            if (providerConfigA.Arguments != null && providerConfigB.Arguments != null)
            {
                throw new DiagnosticToolException($"Provider \"{providerConfigA.Name}\" is declared multiple times with filter arguments.");
            }

            return new EventPipeProvider(providerConfigA.Name, level, providerConfigA.Keywords | providerConfigB.Keywords, providerConfigA.Arguments ?? providerConfigB.Arguments);
        }

        private static void PrintProviders(IReadOnlyList<EventPipeProvider> providers, Dictionary<string, int> enabledBy, IConsole console)
        {
            if (providers.Count == 0)
            {
                console.WriteLine("No .NET providers were configured.");
                console.WriteLine("");
                return;
            }

            console.WriteLine("");
            console.WriteLine(string.Format("{0, -40}", "Provider Name") + string.Format("{0, -20}", "Keywords") +
                string.Format("{0, -20}", "Level") + "Enabled By");  // +4 is for the tab
            foreach (EventPipeProvider provider in providers)
            {
                List<string> providerSources = new();
                if (enabledBy.TryGetValue(provider.Name, out int source))
                {
                    if ((source & (int)ProviderSource.ProvidersArg) == (int)ProviderSource.ProvidersArg)
                    {
                        providerSources.Add("--providers");
                    }
                    if ((source & (int)ProviderSource.CLREventsArg) == (int)ProviderSource.CLREventsArg)
                    {
                        providerSources.Add("--clrevents");
                    }
                    if ((source & (int)ProviderSource.ProfileArg) == (int)ProviderSource.ProfileArg)
                    {
                        providerSources.Add("--profile");
                    }
                }
                console.WriteLine(string.Format("{0, -80}", $"{GetProviderDisplayString(provider)}") + string.Join(", ", providerSources));
            }
            console.WriteLine("");
        }
        private static string GetProviderDisplayString(EventPipeProvider provider) =>
            string.Format("{0, -40}", provider.Name) + string.Format("0x{0, -18}", $"{provider.Keywords:X16}") + string.Format("{0, -8}", provider.EventLevel.ToString() + $"({(int)provider.EventLevel})");

        public static EventPipeProvider ToCLREventPipeProvider(string clreventslist, string clreventlevel)
        {
            if (clreventslist == null || clreventslist.Length == 0)
            {
                return null;
            }

            string[] clrevents = clreventslist.Split("+");
            long clrEventsKeywordsMask = 0;
            for (int i = 0; i < clrevents.Length; i++)
            {
                if (CLREventKeywords.TryGetValue(clrevents[i], out long keyword))
                {
                    clrEventsKeywordsMask |= keyword;
                }
                else
                {
                    throw new DiagnosticToolException($"{clrevents[i]} is not a valid CLR event keyword");
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
            if (int.TryParse(token, out int level) && level >= 0)
            {
                return level > (int)EventLevel.Verbose ? EventLevel.Verbose : (EventLevel)level;
            }

            else
            {
                switch (token.ToLowerInvariant())
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
                        throw new DiagnosticToolException($"Unknown EventLevel: {token}");
                }
            }
        }

        private static EventPipeProvider ToProvider(string provider, IConsole console)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new ArgumentNullException(nameof(provider));
            }

            string[] tokens = provider.Split(new[] { ':' }, 4, StringSplitOptions.None); // Keep empty tokens;

            // Provider name
            string providerName = tokens.Length > 0 ? tokens[0] : null;

            // Check if the supplied provider is a GUID and not a name.
            if (Guid.TryParse(providerName, out _))
            {
                console.WriteLine($"Warning: --provider argument {providerName} appears to be a GUID which is not supported by dotnet-trace. Providers need to be referenced by their textual name.");
            }

            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new DiagnosticToolException("Provider name was not specified.");
            }

            // Keywords
            long keywords = tokens.Length > 1 && !string.IsNullOrWhiteSpace(tokens[1]) ?
                Convert.ToInt64(tokens[1], 16) : defaultKeywords;

            // Level
            EventLevel eventLevel = tokens.Length > 2 && !string.IsNullOrWhiteSpace(tokens[2]) ?
                GetEventLevel(tokens[2]) : defaultEventLevel;

            // Event counters
            string filterData = tokens.Length > 3 ? tokens[3] : null;
            Dictionary<string, string> argument = string.IsNullOrWhiteSpace(filterData) ? null : ParseArgumentString(filterData);
            return new EventPipeProvider(providerName, eventLevel, keywords, argument);
        }

        private static Dictionary<string, string> ParseArgumentString(string argument)
        {
            if (argument == "")
            {
                return null;
            }
            Dictionary<string, string> argumentDict = new();

            int keyStart = 0;
            int keyEnd = 0;
            int valStart = 0;
            int valEnd = 0;
            int curIdx = 0;
            bool inQuote = false;
            argument = Regex.Unescape(argument);
            foreach (char c in argument)
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
                        valStart = curIdx + 1;
                    }
                    else if (c == ';')
                    {
                        valEnd = curIdx;
                        AddKeyValueToArgumentDict(argumentDict, argument, keyStart, keyEnd, valStart, valEnd);
                        keyStart = curIdx + 1; // new key starts
                    }
                    else if (c == '\"')
                    {
                        inQuote = true;
                    }
                }
                curIdx += 1;
            }
            if (valStart > valEnd)
            {
                valEnd = curIdx;
            }
            if (keyStart < keyEnd)
            {
                AddKeyValueToArgumentDict(argumentDict, argument, keyStart, keyEnd, valStart, valEnd);
            }
            return argumentDict;
        }

        private static void AddKeyValueToArgumentDict(Dictionary<string, string> argumentDict, string argument, int keyStart, int keyEnd, int valStart, int valEnd)
        {
            string key = argument.Substring(keyStart, keyEnd - keyStart);
            string val = argument.Substring(valStart, valEnd - valStart);
            if (val.StartsWith("\"") && val.EndsWith("\""))
            {
                val = val.Substring(1, val.Length - 2);
            }
            argumentDict.Add(key, val);
        }
    }
}
