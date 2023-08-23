// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class SymbolGroup : IFileLocator
    {
        private static readonly string s_defaultCacheLocation = Path.Combine(Path.GetTempPath(), "symbols");
        private static volatile FileSymbolCache? s_cache;


        private readonly ImmutableArray<IFileLocator> _groups;

        public SymbolGroup(IEnumerable<IFileLocator> groups)
        {
            _groups = groups.ToImmutableArray();
        }

        public string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            foreach (IFileLocator locator in _groups)
            {
                string? result = locator.FindPEImage(fileName, buildTimeStamp, imageSize, checkProperties);
                if (result != null)
                    return result;
            }

            return null;
        }

        public string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform originalPlatform, bool checkProperties)
        {
            foreach (IFileLocator locator in _groups)
            {
                string? result = locator.FindPEImage(fileName, archivedUnder, buildIdOrUUID, originalPlatform, checkProperties);
                if (result != null)
                    return result;
            }

            return null;
        }

        public string? FindElfImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId, bool checkProperties)
        {
            foreach (IFileLocator locator in _groups)
            {
                string? result = locator.FindElfImage(fileName, archivedUnder, buildId, checkProperties);
                if (result != null)
                    return result;
            }

            return null;
        }

        public string? FindMachOImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> uuid, bool checkProperties)
        {
            foreach (IFileLocator locator in _groups)
            {
                string? result = locator.FindMachOImage(fileName, archivedUnder, uuid, checkProperties);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static FileSymbolCache GetDefaultCache()
        {
            FileSymbolCache? cache = s_cache;
            if (cache != null)
                return cache;

            // We always expect to be able to create a temporary directory
            Directory.CreateDirectory(s_defaultCacheLocation);
            cache = new FileSymbolCache(s_defaultCacheLocation);

            Interlocked.CompareExchange(ref s_cache, cache, null);
            return s_cache!;
        }

        public static IFileLocator CreateFromSymbolPath(string symbolPath)
        {
            FileSymbolCache defaultCache = GetDefaultCache();
            List<IFileLocator> locators = new();

            bool first = false;
            SymbolServer? single = null;

            foreach ((string? Cache, string[] Servers) in symbolPath.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(EnumerateEntries))
            {
                if (Servers.Length == 0)
                    continue;

                FileSymbolCache cache = defaultCache;
                if (Cache != null && !defaultCache.Location.Equals(Cache, FileSymbolCache.IsCaseInsensitiveFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(Cache);
                    cache = new FileSymbolCache(Cache);

                    // if the cache is not the default, we have to add it to the list of locators so we check there before hitting the symbol server
                    locators.Add(cache);
                }

                foreach (string server in Servers)
                {
                    if (server.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || server.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                    {
                        SymbolServer symSvr = new(cache, server);
                        locators.Add(symSvr);

                        if (first)
                        {
                            single = symSvr;
                            first = false;
                        }
                        else
                        {
                            single = null;
                        }
                    }
                    else if (Directory.Exists(server))
                    {
                        locators.Add(new FileSymbolCache(server));
                    }
                    else
                    {
                        Trace.WriteLine($"Ignoring symbol part: {server}");
                    }
                }
            }

            if (single != null)
                return single;

            if (locators.Count == 0)
                return new SymbolServer(defaultCache, SymbolServer.Msdl);

            return new SymbolGroup(locators);
        }

        private static (string? Cache, string[] Servers) EnumerateEntries(string part)
        {
            if (!part.Contains('*'))
                return (null, new string[] { part });

            string[] split = part.Split('*');
            DebugOnly.Assert(split.Length > 1);

            if (split[0].Equals("cache"))
                return (split[1], split.Skip(2).ToArray());


            if (split[0].Equals("symsrv", StringComparison.OrdinalIgnoreCase))
            {
                // We don't really support this, but we'll make it work...ish.
                // Convert symsrv*symstore.dll*DownStream*server -> srv*DownStream*server

                if (split.Length < 3)
                    return (null, new string[] { part });

                split = new string[] { "srv" }.Concat(split.Skip(2)).ToArray();
            }


            if (split[0].Equals("svr", StringComparison.OrdinalIgnoreCase) || split[0].Equals("srv", StringComparison.OrdinalIgnoreCase))
            {
                string? cache = split[1];

                if (string.IsNullOrWhiteSpace(cache))
                    cache = null;

                // e.g. "svr*http://symbols.com/"
                if (split.Length == 2)
                {
                    if (cache is null)
                        return (split[1], split.Skip(2).ToArray());

                    return (null, new string[] { cache });
                }
            }

            // Ok, so we have * but it didn't start with srv or svr, so what now?
            return (null, split.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray());
        }
    }
}
