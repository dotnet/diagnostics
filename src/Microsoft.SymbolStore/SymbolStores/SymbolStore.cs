// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.SymbolStore.SymbolStores
{
    public abstract class SymbolStore : IDisposable
    {
        /// <summary>
        /// Next symbol store to chain if this store refuses the request
        /// </summary>
        public SymbolStore BackingStore { get; }

        /// <summary>
        /// True if this store acquires files from a remote location (for example an HTTP symbol
        /// server). Local stores (cache/directory) return false. Used to skip remote acquisition
        /// when only explicitly-configured local stores should be consulted.
        /// </summary>
        public virtual bool IsRemote => false;

        /// <summary>
        /// Trace/logging source
        /// </summary>
        protected readonly ITracer Tracer;

        public SymbolStore(ITracer tracer)
        {
            Tracer = tracer;
        }

        public SymbolStore(ITracer tracer, SymbolStore backingStore)
            : this(tracer)
        {
            BackingStore = backingStore;
        }

        /// <summary>
        /// Downloads the file or retrieves it from a cache from the symbol store chain.
        /// </summary>
        /// <param name="key">symbol index to retrieve</param>
        /// <param name="token">to cancel requests</param>
        /// <exception cref="InvalidChecksumException">
        /// Thrown for a pdb file when its checksum
        /// does not match the expected value.
        /// </exception>
        /// <returns>file or null if not found</returns>
        public async Task<SymbolStoreFile> GetFile(SymbolStoreKey key, CancellationToken token)
        {
            return await GetFile(key, remoteAllowed: true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the file or retrieves it from a cache from the symbol store chain, optionally
        /// skipping any remote stores (see <see cref="IsRemote"/>) so only explicitly-configured
        /// local stores (cache/directory) are consulted.
        /// </summary>
        /// <param name="key">symbol index to retrieve</param>
        /// <param name="remoteAllowed">if false, remote stores in the chain are skipped</param>
        /// <param name="token">to cancel requests</param>
        /// <returns>file or null if not found</returns>
        public async Task<SymbolStoreFile> GetFile(SymbolStoreKey key, bool remoteAllowed, CancellationToken token)
        {
            SymbolStoreFile file = null;
            if (remoteAllowed || !IsRemote)
            {
                file = await GetFileInner(key, token).ConfigureAwait(false);
            }
            if (file == null)
            {
                if (BackingStore != null)
                {
                    file = await BackingStore.GetFile(key, remoteAllowed, token).ConfigureAwait(false);
                    if (file != null)
                    {
                        await WriteFileInner(key, file).ConfigureAwait(false);
                    }
                }
            }
            if (file != null)
            {
                // Reset stream to the beginning because the stream may have
                // been read or written by the symbol store implementation.
                file.Stream.Position = 0;
            }
            return file;
        }

        protected virtual Task<SymbolStoreFile> GetFileInner(SymbolStoreKey key, CancellationToken token)
        {
            return Task.FromResult<SymbolStoreFile>(null);
        }

        protected virtual Task WriteFileInner(SymbolStoreKey key, SymbolStoreFile file)
        {
            return Task.FromResult(0);
        }

        public virtual void Dispose()
        {
            BackingStore?.Dispose();
        }

        /// <summary>
        /// Compares two file paths using OS specific casing.
        /// </summary>
        internal static bool IsPathEqual(string path1, string path2)
        {
#if !NET462
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return string.Equals(path1, path2);
            }
#endif
            return StringComparer.OrdinalIgnoreCase.Equals(path1, path2);
        }

        internal static int HashPath(string path)
        {
#if !NET462
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return path.GetHashCode();
            }
#endif
            return StringComparer.OrdinalIgnoreCase.GetHashCode(path);
        }
    }
}
