// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    /// <summary>
    /// Verifies that a dump whose runtime module was loaded from a remote (UNC/device) path
    /// never causes SOS to probe or load the DAC, DBI or cDAC from that location.
    /// </summary>
    public class RuntimePathPolicyTests : IDisposable
    {
        private const ulong RuntimeBase = 0x20000;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "RuntimePathPolicyTests-" + Path.GetRandomFileName());

        public RuntimePathPolicyTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public void DacIsResolvedFromLocalRuntimeDirectory()
        {
            WriteDebugLibraries(_directory);
            using RuntimeDiscoveryTestTarget target = CreateTarget(_directory);

            IRuntime runtime = Assert.Single(target.Enumerate());

            Assert.Equal(Path.Combine(_directory, DacName), runtime.GetDacFilePath(out _));
            Assert.Equal(Path.Combine(_directory, DbiName), runtime.GetDbiFilePath());
        }

        [Fact]
        public void DacIsNotLoadedFromRemoteRuntimeDirectory()
        {
            WriteDebugLibraries(_directory);
            using RuntimeDiscoveryTestTarget target = CreateTarget(RemotePathTestHelpers.ToRemoteForm(_directory));

            IRuntime runtime = Assert.Single(target.Enumerate());

            Assert.Null(runtime.GetDacFilePath(out _));
        }

        [Fact]
        public void DbiIsNotLoadedFromRemoteRuntimeDirectory()
        {
            WriteDebugLibraries(_directory);
            using RuntimeDiscoveryTestTarget target = CreateTarget(RemotePathTestHelpers.ToRemoteForm(_directory));

            IRuntime runtime = Assert.Single(target.Enumerate());

            Assert.Null(runtime.GetDbiFilePath());
        }

        [Fact]
        public void CDacCandidateIsHostLocalForRemoteRuntimeDirectory()
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget(RemotePathTestHelpers.ToRemoteForm(_directory));

            IRuntime runtime = Assert.Single(target.Enumerate());
            ClrInfo info = runtime.Services.GetService<ClrInfo>();

            DebugLibraryInfo cdac = Assert.Single(info.DebuggingLibraries, library => library.Kind == DebugLibraryKind.CDac);
            Assert.StartsWith(AppContext.BaseDirectory, cdac.FileName, StringComparison.Ordinal);
            Assert.True(Path.IsPathFullyQualified(cdac.FileName));
        }

        [Theory]
        [InlineData(@"\\remote.example\share")]
        [InlineData("//remote.example/share")]
        public void RemoteRuntimeModuleDirectoryIsRejected(string directory)
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget(_directory);
            IRuntime runtime = Assert.Single(target.Enumerate());

            Assert.Throws<ArgumentException>(() => runtime.RuntimeModuleDirectory = directory);
            Assert.Null(runtime.RuntimeModuleDirectory);
        }

        [Fact]
        public void RemoteRuntimeModuleDirectoryIsNotProbed()
        {
            string runtimeDirectory = Path.Combine(_directory, "runtime");
            Directory.CreateDirectory(runtimeDirectory);
            WriteDebugLibraries(_directory);
            using RuntimeDiscoveryTestTarget target = CreateTarget(runtimeDirectory);
            IRuntime runtime = Assert.Single(target.Enumerate());

            try
            {
                runtime.RuntimeModuleDirectory = RemotePathTestHelpers.ToRemoteForm(_directory);
            }
            catch (ArgumentException)
            {
            }

            Assert.Null(runtime.GetDacFilePath(out _));
            Assert.Null(runtime.GetDbiFilePath());
        }

        [Fact]
        public void SymbolStoreResultIsNotRejected()
        {
            string runtimeDirectory = Path.Combine(_directory, "runtime");
            Directory.CreateDirectory(runtimeDirectory);
            string storeFile = Path.Combine(_directory, "downloaded.dll");
            File.WriteAllBytes(storeFile, [1, 2, 3, 4]);
            string storeResult = RemotePathTestHelpers.ToRemoteForm(storeFile);
            using RuntimeDiscoveryTestTarget target = CreateTarget(runtimeDirectory);
            target.Symbols.FileDownloader = (_, _) => storeResult;

            IRuntime runtime = Assert.Single(target.Enumerate());

            // Symbol-store results come from user-configured stores and are trusted (matches ClrMD);
            // signature verification of the loaded copy happens at load time.
            Assert.Equal(storeResult, runtime.GetDacFilePath(out _));
            Assert.Equal(storeResult, runtime.GetDbiFilePath());
            Assert.NotEmpty(target.Symbols.FileRequests);
        }

        private static string RuntimeName => Platform == OSPlatform.Windows ? "coreclr.dll"
            : Platform == OSPlatform.Linux ? "libcoreclr.so" : "libcoreclr.dylib";

        private static string DacName => Platform == OSPlatform.Windows ? "mscordaccore.dll"
            : Platform == OSPlatform.Linux ? "libmscordaccore.so" : "libmscordaccore.dylib";

        private static string DbiName => Platform == OSPlatform.Windows ? "mscordbi.dll"
            : Platform == OSPlatform.Linux ? "libmscordbi.so" : "libmscordbi.dylib";

        private static OSPlatform Platform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux
            : OSPlatform.OSX;

        private static RuntimeDiscoveryTestTarget CreateTarget(string runtimeDirectory)
        {
            // Debugging libraries are only considered for the host platform and architecture.
            RuntimeDiscoveryTestTarget target = new(Platform, RuntimeInformation.ProcessArchitecture);
            target.AddCoreRuntime(RuntimeBase, fileName: runtimeDirectory + Path.DirectorySeparatorChar + RuntimeName);
            target.Symbols.FileDownloader ??= (_, _) => null;
            return target;
        }

        private static void WriteDebugLibraries(string directory)
        {
            File.WriteAllBytes(Path.Combine(directory, RuntimeName), [0]);
            File.WriteAllBytes(Path.Combine(directory, DacName), [0]);
            File.WriteAllBytes(Path.Combine(directory, DbiName), [0]);
        }
    }

    internal static class RemotePathTestHelpers
    {
        /// <summary>
        /// Returns a remote/device form of a local path (\\?\C:\... on Windows, //tmp/... elsewhere).
        /// It is rejected like a UNC path but still resolves to the local file, so tests can prove
        /// the file is never probed without depending on network access.
        /// </summary>
        public static string ToRemoteForm(string localPath) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\?\" + localPath : "/" + localPath;
    }
}
