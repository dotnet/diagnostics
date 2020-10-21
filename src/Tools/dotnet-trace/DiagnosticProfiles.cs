using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal class DiagnosticProfileProvider
    {
        public string Name;
        public EventLevel Level;
        public long Keywords;
    }

    internal class DiagnosticProfile
    {
        public DiagnosticProfileProvider[] Providers { get; set; }
        public string Description { get; set; }

        public static Dictionary<string, DiagnosticProfile> AllProfiles = new Dictionary<string, DiagnosticProfile>()
        {
            {
                "gc-pause",
                new DiagnosticProfile
                {
                    Providers = new DiagnosticProfileProvider[] {
                        new DiagnosticProfileProvider { Name = "Microsoft-Windows-DotNETRuntime", Level = EventLevel.Informational, Keywords = (long)ClrTraceEventParser.Keywords.GC }
                    },
                    Description = "Monitor GC pauses and runtime suspensions"
                }
            },

            {
                "loader-binder",
                new DiagnosticProfile
                {
                    Providers = new DiagnosticProfileProvider[]
                    {
                        new DiagnosticProfileProvider { Name = "Microsoft-Windows-DotNETRuntime", Level = EventLevel.Informational, Keywords = ((long)ClrTraceEventParser.Keywords.Binder | (long)ClrTraceEventParser.Keywords.Loader) }
                    },
                    Description = "Monitor assembly loader and binder logs"
                }
            }
            // TODO: Add more diagnosic profiles here.
        };
    }

    internal class DiagnosticProfileBuilder
    {
        public static List<EventPipeProvider> GetProfileProviders(string profiles)
        {
            Dictionary<string, DiagnosticProfileProvider> providers = new Dictionary<string, DiagnosticProfileProvider>();

            if (profiles == null)
            {
                return null;
            }

            string[] profileCandidates = profiles.Split(',');

            foreach (string profile in profileCandidates)
            {
                if (DiagnosticProfile.AllProfiles.TryGetValue(profile.ToLowerInvariant(), out DiagnosticProfile diagnosticProfile))
                {
                    foreach (DiagnosticProfileProvider diagnosticProfileProvider in diagnosticProfile.Providers)
                    {
                        if (providers.ContainsKey(diagnosticProfileProvider.Name))
                        {
                            if (providers[diagnosticProfileProvider.Name].Level < diagnosticProfileProvider.Level)
                            {
                                providers[diagnosticProfileProvider.Name].Level = diagnosticProfileProvider.Level;
                            }

                            providers[diagnosticProfileProvider.Name].Keywords |= diagnosticProfileProvider.Keywords;
                        }
                        else
                        {
                            providers[diagnosticProfileProvider.Name] = diagnosticProfileProvider;
                        }
                    }

                }
                else
                {
                    throw new ArgumentException($"{profile} is not a valid diagnostic profile.");
                }
            }

            List<EventPipeProvider> eventPipeProviders = new List<EventPipeProvider>();
            foreach ((string name, DiagnosticProfileProvider provider) in providers)
            {
                eventPipeProviders.Add(new EventPipeProvider(provider.Name, provider.Level, provider.Keywords));
            }
            return eventPipeProviders;
        }
    }
}
