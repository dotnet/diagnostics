// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.SymbolStore.SymbolStores
{
    public sealed class CacheSymbolStore : SymbolStore
    {
        public string CacheDirectory { get; }

        public CacheSymbolStore(ITracer tracer, SymbolStore backingStore, string cacheDirectory)
            : base(tracer, backingStore)
        {
            CacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
        }

        protected override Task<SymbolStoreFile> GetFileInner(SymbolStoreKey key, CancellationToken token)
        {
            SymbolStoreFile result = null;
            string cacheFile = GetCacheFilePath(key);
            if (File.Exists(cacheFile))
            {
                Stream fileStream = File.OpenRead(cacheFile);
                result = new SymbolStoreFile(fileStream, cacheFile);
            }
            return Task.FromResult(result);
        }

        protected override async Task WriteFileInner(SymbolStoreKey key, SymbolStoreFile file)
        {
            string cacheFile = GetCacheFilePath(key);
            if (cacheFile != null && !File.Exists(cacheFile))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                    using (Stream destinationStream = File.OpenWrite(cacheFile))
                    {
                        await file.Stream.CopyToAsync(destinationStream).ConfigureAwait(false);
                        Tracer.Verbose("Cached: {0}", cacheFile);
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is UnauthorizedAccessException || ex is IOException)
                {
                }
            }
        }

        private string GetCacheFilePath(SymbolStoreKey key)
        {
            if (SymbolStoreKey.IsKeyValid(key.Index)) {
                return Path.Combine(CacheDirectory, key.Index);
            }
            Tracer.Error("CacheSymbolStore: invalid key index {0}", key.Index);
            return null;
        }

        public override bool Equals(object obj)
        {
            if (obj is CacheSymbolStore store)
            {
                return IsPathEqual(CacheDirectory, store.CacheDirectory);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashPath(CacheDirectory);
        }

        public override string ToString()
        {
            return $"Cache: {CacheDirectory}";
        }
    }
}
