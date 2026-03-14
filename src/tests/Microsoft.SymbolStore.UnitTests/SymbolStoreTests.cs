// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;
using SOS;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.SymbolStore.Tests
{
    public class SymbolStoreTests
    {
        readonly ITracer _tracer;

        public SymbolStoreTests(ITestOutputHelper output)
        {
            _tracer = new Tracer(output);
        }

        [Fact]
        public async Task CacheSymbolStore()
        {
            using (Stream pdb = File.OpenRead("TestBinaries/HelloWorld.pdb")) {
                // Clean up any previous cache directories
                string cacheDirectory = "TestSymbolCache";
                try {
                    Directory.Delete(cacheDirectory, recursive: true);
                }
                catch (DirectoryNotFoundException) {
                }
                var inputFile = new SymbolStoreFile(pdb, "HelloWorld.pdb");
                var generator = new PDBFileKeyGenerator(_tracer, inputFile);

                IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(keys.Count() == 1);
                SymbolStoreKey key = keys.First();

                var backingStore = new TestSymbolStore(_tracer, key, inputFile);
                var cacheSymbolStore = new CacheSymbolStore(_tracer, backingStore, cacheDirectory);

                // This should put HelloWorld.pdb into the cache
                SymbolStoreFile outputFile = await cacheSymbolStore.GetFile(key, CancellationToken.None);
                Assert.True(outputFile != null);

                // Should be the exact same instance given to TestSymbolStore
                Assert.True(inputFile == outputFile);

                // This should get it from the cache and not the backingStore
                backingStore.Dispose();
                outputFile = await cacheSymbolStore.GetFile(key, CancellationToken.None);
                Assert.True(outputFile != null);

                // Should NOT be the exact same SymbolStoreFile instance given to TestSymbolStore
                Assert.True(inputFile != outputFile);

                // Now make sure the output file from the cache is the same as the pdb we opened above
                CompareStreams(pdb, outputFile.Stream);
            }
        }

        [Fact]
        public async Task DirectorySymbolStore()
        {
            using (Stream pdb = File.OpenRead("TestBinaries/dir1/System.Threading.Thread.pdb"))
            {
                var inputFile = new SymbolStoreFile(pdb, "System.Threading.Thread.pdb");
                var generator = new PortablePDBFileKeyGenerator(_tracer, inputFile);

                IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.IdentityKey);
                Assert.True(keys.Count() == 1);
                SymbolStoreKey key = keys.First();

                var dir1store = new DirectorySymbolStore(_tracer, null, "TestBinaries/dir1");
                var dir2store = new DirectorySymbolStore(_tracer, dir1store, "TestBinaries/dir2");

                SymbolStoreFile outputFile = await dir2store.GetFile(key, CancellationToken.None);
                Assert.True(outputFile != null);

                // Should NOT be the exact same SymbolStoreFile instance
                Assert.True(inputFile != outputFile);

                CompareStreams(pdb, outputFile.Stream);
            }
        }

        [Fact]
        public async Task HttpSymbolStore()
        {
            using (FileStream downloadStream = File.OpenRead("TestBinaries/dir1/System.Threading.Thread.dll"))
            {
                using (Stream compareStream = File.OpenRead("TestBinaries/dir1/System.Threading.Thread.pdb"))
                {
                    await DownloadFile(downloadStream, compareStream, flags: KeyTypeFlags.SymbolKey);
                }
            }
        }

        private async Task DownloadFile(FileStream downloadStream, Stream compareStream, KeyTypeFlags flags)
        {
            SymbolStoreFile file = new SymbolStoreFile(downloadStream, downloadStream.Name);

            Uri.TryCreate("https://msdl.microsoft.com/download/symbols/", UriKind.Absolute, out Uri uri);
            SymbolStores.SymbolStore store = new HttpSymbolStore(_tracer, backingStore: null, uri);

            var generator = new FileKeyGenerator(_tracer, file);

            IEnumerable<SymbolStoreKey> keys = generator.GetKeys(flags);
            Assert.True(keys.Count() > 0);

            foreach (SymbolStoreKey key in keys)
            {
                if (key.FullPathName.Contains(".ni.pdb")) {
                    continue;
                }
                using (SymbolStoreFile symbolFile = await store.GetFile(key, CancellationToken.None))
                {
                    if (symbolFile != null)
                    {
                        Assert.True(downloadStream != symbolFile.Stream);
                        Assert.True(compareStream != symbolFile.Stream);

                        compareStream.Seek(0, SeekOrigin.Begin);
                        CompareStreams(compareStream, symbolFile.Stream);
                    }
                }
            }
        }

        private void CompareStreams(Stream stream1, Stream stream2)
        {
            Assert.True(stream1.Length == stream2.Length);

            stream1.Position = 0;
            stream2.Position = 0;

            for (int i = 0; i < stream1.Length; i++) {
                int b1 = stream1.ReadByte();
                int b2 = stream2.ReadByte();
                Assert.True(b1 == b2);
                if (b1 != b2) {
                    break;
                }
            }
        }

        sealed class TestSymbolStore : Microsoft.SymbolStore.SymbolStores.SymbolStore
        {
            readonly SymbolStoreKey _key;
            SymbolStoreFile _file;

            public TestSymbolStore(ITracer tracer, SymbolStoreKey key, SymbolStoreFile file)
                : base(tracer)
            {
                _key = key;
                _file = file;
            }

            protected override Task<SymbolStoreFile> GetFileInner(SymbolStoreKey key, CancellationToken token)
            {
                if (_file != null && key.Equals(_key))
                {
                    _file.Stream.Position = 0;
                    return Task.FromResult(_file);
                }
                return Task.FromResult<SymbolStoreFile>(null);
            }

            public override void Dispose()
            {
                _file = null;
                base.Dispose();
            }
        }
    }
}
