using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Tools.Collect
{
    internal static class KnownData
    {
        private static readonly IReadOnlyDictionary<string, KnownProvider> _knownProviders =
            CreateKnownProviders().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, CollectionProfile> _knownProfiles =
            CreateProfiles().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<KnownProvider> CreateKnownProviders()
        {
            yield return CreateClrProvider();
        }

        private static IEnumerable<CollectionProfile> CreateProfiles()
        {
            yield return new CollectionProfile(
                CollectionProfile.DefaultProfileName,
                "A default set of event providers useful for diagosing problems in any .NET application.",
                new[] {
                    new EventSpec(ClrTraceEventParser.ProviderName, (ulong)ClrTraceEventParser.Keywords.Default, EventLevel.Informational)
                },
                Array.Empty<LoggerSpec>());

            yield return new CollectionProfile(
                "AspNetCore",
                "A set of event providers useful for diagnosing problems in ASP.NET Core applications.",
                new[]
                {
                    new EventSpec("Microsoft-AspNetCore-Hosting", ulong.MaxValue, EventLevel.Informational),
                },
                Array.Empty<LoggerSpec>());

            yield return new CollectionProfile(
                "Kestrel",
                "Detailed events for ASP.NET Core Kestrel",
                new[]
                {
                    new EventSpec("Microsoft-AspNetCore-Server-Kestrel", ulong.MaxValue, EventLevel.Verbose),
                },
                Array.Empty<LoggerSpec>());
        }

        private static KnownProvider CreateClrProvider()
        {
            return new KnownProvider(
                ClrTraceEventParser.ProviderName,
                ClrTraceEventParser.ProviderGuid,
                ScanKeywordType(typeof(ClrTraceEventParser.Keywords)));
        }

        public static IReadOnlyList<CollectionProfile> GetAllProfiles() => _knownProfiles.Values.ToList();
        public static IReadOnlyList<KnownProvider> GetAllProviders() => _knownProviders.Values.ToList();

        public static bool TryGetProvider(string providerName, out KnownProvider provider) => _knownProviders.TryGetValue(providerName, out provider);
        public static bool TryGetProfile(string profileName, out CollectionProfile profile) => _knownProfiles.TryGetValue(profileName, out profile);

        private static IEnumerable<KnownKeyword> ScanKeywordType(Type keywordType)
        {
            var values = Enum.GetValues(keywordType).Cast<long>().ToList();
            var keywords = values.Distinct().Select(v => new KnownKeyword(Enum.GetName(keywordType, v), (ulong)v)).ToList();
            return keywords;
        }
    }

    internal class KnownProvider
    {
        public string Name { get; }
        public Guid Guid { get; }
        public IReadOnlyDictionary<string, KnownKeyword> Keywords { get; }

        public KnownProvider(string name, Guid guid, IEnumerable<KnownKeyword> keywords)
        {
            Name = name;
            Guid = guid;
            Keywords = keywords.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal class KnownKeyword
    {
        public string Name { get; }
        public ulong Value { get; }

        public KnownKeyword(string name, ulong value)
        {
            Name = name;
            Value = value;
        }
    }
}
