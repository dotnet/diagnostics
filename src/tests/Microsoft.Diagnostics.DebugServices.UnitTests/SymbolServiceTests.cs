// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.SymbolStores;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    /// <summary>
    /// Test the service event implementation
    /// </summary>
    public class SymbolServiceTests : IHost, IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "SymbolServiceTests-" + Guid.NewGuid().ToString("N"));

        public SymbolServiceTests()
        {
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
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

        [Fact]
        public void DownloadFileStagesRemoteStoreFileLocally()
        {
            byte[] contents = new byte[] { 1, 2, 3, 4 };
            SymbolService symbolService = new(this);
            symbolService.SetSymbolStore(new TestSymbolStore(contents, @"\\server\share\mscordaccore.dll"));

            string filePath = symbolService.DownloadFile("mscordaccore.dll/test", "mscordaccore.dll");

            Assert.True(PathUtilities.IsSafeAbsoluteLocalPath(filePath));
            Assert.StartsWith(_tempDirectory, filePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(contents, File.ReadAllBytes(filePath));
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType => HostType.DotnetDump;

        public IServiceProvider Services => throw new NotImplementedException();

        public IEnumerable<ITarget> EnumerateTargets() => throw new NotImplementedException();

        public int AddTarget(ITarget target) => throw new NotImplementedException();

        public string GetTempDirectory()
        {
            Directory.CreateDirectory(_tempDirectory);
            return _tempDirectory;
        }

        #endregion

        private sealed class TestSymbolStore : Microsoft.SymbolStore.SymbolStores.SymbolStore
        {
            private readonly byte[] _contents;
            private readonly string _fileName;

            public TestSymbolStore(byte[] contents, string fileName)
                : base(Microsoft.Diagnostics.DebugServices.Implementation.Tracer.Instance)
            {
                _contents = contents;
                _fileName = fileName;
            }

            protected override Task<SymbolStoreFile> GetFileInner(SymbolStoreKey key, CancellationToken token)
            {
                Stream stream = new MemoryStream(_contents, writable: false);
                return Task.FromResult(new SymbolStoreFile(stream, _fileName));
            }
        }
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
