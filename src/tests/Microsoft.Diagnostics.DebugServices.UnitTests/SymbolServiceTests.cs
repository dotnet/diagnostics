// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    /// <summary>
    /// Test the service event implementation
    /// </summary>
    public class SymbolServiceTests : IHost
    {
        public SymbolServiceTests()
        {
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void DownloadELFFullSymbolFile(bool cached, bool mismatchedBuildId, bool hasDebugLink)
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                byte[] contents = File.ReadAllBytes(Path.Combine("TestBinaries", "symbolized_executable"));
                using ELFFile elf = new(new StreamAddressSpace(new MemoryStream(contents)));
                byte[] buildId = elf.BuildID;
                if (mismatchedBuildId)
                {
                    buildId[0] ^= 0xff;
                }
                string fileName = cached ? "module" : Path.Combine(directory, "module");
                string targetName = hasDebugLink ? fileName + ".full" : fileName + ".dbg";
                TestModule module = new(hasDebugLink ? targetName : null, buildId, fileName);
                SymbolService symbolService = new(this);
                string expectedPath = targetName;
                if (cached)
                {
                    SymbolStoreKey key = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, targetName,
                        buildId, symbolFile: false, symbolFileName: null).Single();
                    expectedPath = Path.Combine(directory, key.Index);
                    Directory.CreateDirectory(Path.GetDirectoryName(expectedPath));
                    symbolService.AddCachePath(directory);
                }
                File.WriteAllBytes(expectedPath, contents);

                Assert.Equal(mismatchedBuildId ? null : expectedPath, symbolService.DownloadSymbolFile(module));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DownloadELFSeparateDebugFile(bool cached)
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                using FileStream compressed = File.OpenRead(Path.Combine("TestBinaries", "libcoreclrtraceptprovider.so.dbg.gz"));
                using GZipStream decompressed = new(compressed, CompressionMode.Decompress);
                using MemoryStream contents = new();
                decompressed.CopyTo(contents);
                using ELFFile elf = new(new StreamAddressSpace(contents));
                string targetName = cached ? "module.dbg" : Path.Combine(directory, "module.dbg");
                TestModule module = new(targetName, elf.BuildID);
                SymbolService symbolService = new(this);
                string expectedPath = targetName;
                if (cached)
                {
                    SymbolStoreKey key = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.SymbolKey, module.FileName,
                        elf.BuildID, symbolFile: false, symbolFileName: targetName).Single();
                    expectedPath = Path.Combine(directory, key.Index);
                    Directory.CreateDirectory(Path.GetDirectoryName(expectedPath));
                    symbolService.AddCachePath(directory);
                }
                File.WriteAllBytes(expectedPath, contents.ToArray());
                Assert.Equal(expectedPath, symbolService.DownloadSymbolFile(module));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void DownloadELFMissingSymbolFile()
        {
            TestModule module = new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
                Enumerable.Range(1, 20).Select(value => (byte)value).ToArray());
            SymbolService symbolService = new(this);
            Assert.Null(symbolService.DownloadSymbolFile(module));
        }

        [Theory]
        [InlineData("symbolized_executable", false)]
        [InlineData("symbolized_executable", true)]
        [InlineData("stripped_executable", false)]
        [InlineData("stripped_executable", true)]
        public void DownloadELFModule(string fixture, bool cached)
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                byte[] contents = File.ReadAllBytes(Path.Combine("TestBinaries", fixture));
                using ELFFile elf = new(new StreamAddressSpace(new MemoryStream(contents)));
                string fileName = cached ? "module" : Path.Combine(directory, "module");
                TestModule module = new(null, elf.BuildID, fileName);
                SymbolService symbolService = new(this);
                string expectedPath = fileName;
                if (cached)
                {
                    SymbolStoreKey key = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, fileName,
                        elf.BuildID, symbolFile: false, symbolFileName: null).Single();
                    expectedPath = Path.Combine(directory, key.Index);
                    Directory.CreateDirectory(Path.GetDirectoryName(expectedPath));
                    symbolService.AddCachePath(directory);
                }
                File.WriteAllBytes(expectedPath, contents);
                Assert.Equal(expectedPath, symbolService.DownloadModuleFile(module));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void DownloadELFDebugLinkPrefersSymbolKey()
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                byte[] contents = File.ReadAllBytes(Path.Combine("TestBinaries", "symbolized_executable"));
                using ELFFile elf = new(new StreamAddressSpace(new MemoryStream(contents)));
                TestModule module = new("module.full", elf.BuildID);
                SymbolStoreKey symbolKey = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.SymbolKey, module.FileName,
                    elf.BuildID, symbolFile: false, module.GetSymbolFileName()).Single();
                SymbolStoreKey moduleKey = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, module.GetSymbolFileName(),
                    elf.BuildID, symbolFile: false, symbolFileName: null).Single();
                foreach (SymbolStoreKey key in new[] { symbolKey, moduleKey })
                {
                    string fileName = Path.Combine(directory, key.Index);
                    Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                    File.WriteAllBytes(fileName, contents);
                }
                SymbolService symbolService = new(this);
                symbolService.AddCachePath(directory);

                // Previously uploaded full binaries may still be stored under the symbol-only key.
                Assert.Equal(Path.Combine(directory, symbolKey.Index), symbolService.DownloadSymbolFile(module));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class TestModule : IModule
        {
            private readonly string _symbolFileName;

            public TestModule(string symbolFileName, byte[] buildId, string fileName = "module")
            {
                _symbolFileName = symbolFileName;
                BuildId = ImmutableArray.CreateRange(buildId);
                FileName = fileName;
            }

            public ITarget Target { get; } = new TestTarget();
            public string FileName { get; }
            public ImmutableArray<byte> BuildId { get; }
            public uint? IndexFileSize => null;
            public uint? IndexTimeStamp => null;
            public IEnumerable<PdbFileInfo> GetPdbFileInfos() => Array.Empty<PdbFileInfo>();
            public string GetSymbolFileName() => _symbolFileName;
            public IServiceProvider Services => throw new NotImplementedException();
            public int ModuleIndex => throw new NotImplementedException();
            public ulong ImageBase => throw new NotImplementedException();
            public ulong ImageSize => throw new NotImplementedException();
            public bool IsPEImage => false;
            public bool IsManaged => false;
            public bool? IsFileLayout => null;
            public Version GetVersionData() => throw new NotImplementedException();
            public string GetVersionString() => throw new NotImplementedException();
            public string LoadSymbols() => throw new NotImplementedException();
            public ImmutableArray<byte> GetMetadata() => throw new NotImplementedException();
        }

        private sealed class TestTarget : ITarget
        {
            public OSPlatform OperatingSystem => OSPlatform.Linux;
            public IHost Host => throw new NotImplementedException();
            public int Id => throw new NotImplementedException();
            public Architecture Architecture => Architecture.X64;
            public bool IsDump => true;
            public uint? ProcessId => null;
            public IServiceProvider Services => throw new NotImplementedException();
            public IServiceEvent OnFlushEvent => throw new NotImplementedException();
            public IServiceEvent OnDestroyEvent => throw new NotImplementedException();
            public void Flush() => throw new NotImplementedException();
            public void Destroy() => throw new NotImplementedException();
        }

        [Fact]
        public void SymbolPathTests()
        {
            SymbolService symbolService = new(this);
            Assert.False(symbolService.ParseSymbolPath("srv"));
            Assert.False(symbolService.ParseSymbolPath("cache"));
            Assert.False(symbolService.ParseSymbolPath("symsrv"));

            string defaultServer = $"Server: {SymbolService.MsdlSymbolServer}";
            string defaultPath = $"Cache: {symbolService.DefaultSymbolCache} {defaultServer}";
            string localSymbolCache = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "c:\\localsymbolcache" : "/home/foo/localsymbolcache";

            Assert.True(symbolService.ParseSymbolPath("srv*"));
            Assert.Equal(defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("srv**"));
            Assert.Equal(defaultPath, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("symsrv*symsrv.dll*"));
            Assert.Equal(defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("cache*;srv*"));
            Assert.Equal(defaultPath, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath("srv*https://msdl.microsoft.com/download/symbols/"));
            Assert.Equal(defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"srv**{SymbolService.MsdlSymbolServer}"));
            Assert.Equal(defaultPath, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"srv*{localSymbolCache}*https://symweb/"));
            string testpath1 = $"Cache: {localSymbolCache} Server: https://symweb/";
            Assert.Equal(testpath1, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"cache*{localSymbolCache};srv*"));
            string testpath2 = $"Cache: {localSymbolCache} Server: {SymbolService.MsdlSymbolServer}";
            Assert.Equal(testpath2, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            Assert.True(symbolService.ParseSymbolPath($"srv**{localSymbolCache}*http://msdl.microsoft.com/download/symbols/"));
            Assert.Equal($"Cache: {symbolService.DefaultSymbolCache} Cache: {localSymbolCache} Server: http://msdl.microsoft.com/download/symbols/", symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.True(symbolService.ParseSymbolPath($"symsrv*symsrv.dll*{localSymbolCache}*\\\\server\\share"));
                Assert.Equal($"Cache: {localSymbolCache} Cache: \\\\server\\share", symbolService.FormatSymbolStores());
                symbolService.DisableSymbolStore();

                Assert.True(symbolService.ParseSymbolPath("symsrv*symsrv.dll*d:\\data\\SYM\\symcache*\\\\aw0eus0symcache.file.core.windows.net\\Symbols*http://localhost/remote200/30e07e1454924e55901d7f693f7eddf1/0/x64/4542784547/remote"));
                Assert.Equal("Cache: d:\\data\\SYM\\symcache Cache: \\\\aw0eus0symcache.file.core.windows.net\\Symbols Server: http://localhost/remote200/30e07e1454924e55901d7f693f7eddf1/0/x64/4542784547/remote/", symbolService.FormatSymbolStores());
                symbolService.DisableSymbolStore();
            }

            string symbolDirectory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "c:\\symbols\\" : "/home/foo/symbols/";
            Assert.True(symbolService.ParseSymbolPath(symbolDirectory));
            Assert.Equal($"Directory: {symbolDirectory}", symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();

            string symbolDirectory2 = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "c:\\foo\\bar" : "/home/foo/bar";
            Assert.True(symbolService.ParseSymbolPath($"{symbolDirectory};{symbolDirectory2};srv*"));
            Assert.Equal($"Directory: {symbolDirectory} Directory: {symbolDirectory2} " + defaultServer, symbolService.FormatSymbolStores());
            symbolService.DisableSymbolStore();
        }

        [Fact]
        public void OpenSymbolFile_ReturnsNull_ForInvalidPEStream()
        {
            SymbolService symbolService = new(this);

            // OpenSymbolFile should return null (not throw) for non-PE data.
            // The native SymbolReader::LoadSymbols layer caches these failures
            // to avoid repeated symbol server lookups (dotnet/diagnostics#675),
            // but that caching is in native C++ and cannot be tested here.
            byte[] bogusData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
            using System.IO.MemoryStream stream = new(bogusData);

            ISymbolFile result = symbolService.OpenSymbolFile("bogus.dll", isFileLayout: false, stream);
            Assert.Null(result);
        }

        [Fact]
        public void OpenSymbolFile_ReturnsNull_ForInvalidPdbStream()
        {
            SymbolService symbolService = new(this);

            // A stream that is not a valid portable PDB should return null.
            byte[] bogusData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00 };
            using System.IO.MemoryStream stream = new(bogusData);

            ISymbolFile result = symbolService.OpenSymbolFile(stream);
            Assert.Null(result);
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType => HostType.DotnetDump;

        public IServiceProvider Services => throw new NotImplementedException();

        public IEnumerable<ITarget> EnumerateTargets() => throw new NotImplementedException();

        public int AddTarget(ITarget target) => throw new NotImplementedException();

        public string GetTempDirectory() => throw new NotImplementedException();

        #endregion
    }

    public static class SymbolServiceExtensions
    {
        public static string FormatSymbolStores(this SymbolService symbolService)
        {
            StringBuilder sb = new();
            symbolService.ForEachSymbolStore<SymbolStore.SymbolStores.SymbolStore>((symbolStore) => sb.AppendLine(symbolStore.ToString()));
            return sb.ToString().Replace(Environment.NewLine, " ").TrimEnd();
        }
    }
}
