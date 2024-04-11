// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;

namespace SOS
{
    /// <summary>
    /// Basic http symbol store. The request can be authentication with a PAT for VSTS symbol stores.
    /// </summary>
    public class DirectorySymbolStore : SymbolStore
    {
        /// <summary>
        /// Directory to search symbols
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// Create an instance of a directory symbol store
        /// </summary>
        /// <param name="backingStore">next symbol store or null</param>
        /// <param name="directory">symbol search path</param>
        public DirectorySymbolStore(ITracer tracer, SymbolStore backingStore, string directory)
            : base(tracer, backingStore)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        protected override Task<SymbolStoreFile> GetFileInner(SymbolStoreKey key, CancellationToken token)
        {
            SymbolStoreFile result = null;

            if (SymbolStoreKey.IsKeyValid(key.Index))
            {
                string filePath = Path.Combine(Directory, Path.GetFileName(key.FullPathName));
                if (File.Exists(filePath))
                {
                    try
                    {
                        Stream fileStream = File.OpenRead(filePath);
                        SymbolStoreFile file = new(fileStream, filePath);
                        FileKeyGenerator generator = new(Tracer, file);

                        foreach (SymbolStoreKey targetKey in generator.GetKeys(KeyTypeFlags.IdentityKey))
                        {
                            if (key.Equals(targetKey))
                            {
                                result = file;
                                break;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                    {
                    }
                }
            }
            else
            {
                Tracer.Error("DirectorySymbolStore: invalid key index {0}", key.Index);
            }

            return Task.FromResult(result);
        }

        public override bool Equals(object obj)
        {
            if (obj is DirectorySymbolStore store)
            {
                return IsPathEqual(Directory, store.Directory);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashPath(Directory);
        }

        public override string ToString()
        {
            return $"Directory: {Directory}";
        }
    }
}
