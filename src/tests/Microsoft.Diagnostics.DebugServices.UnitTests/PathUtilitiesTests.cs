// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class PathUtilitiesTests
    {
        [Theory]
        [InlineData(@"\\remote.example\share\mscordaccore.dll")]
        [InlineData("//remote.example/share/mscordaccore.dll")]
        [InlineData(@"\/remote/share/mscordaccore.dll")]
        [InlineData(@"/\remote/share/mscordaccore.dll")]
        [InlineData(@"\\?\C:\Windows\mscordaccore.dll")]
        [InlineData(@"\\?\UNC\srv\share\mscordaccore.dll")]
        [InlineData(@"\\.\PIPE\mscordaccore")]
        [InlineData(@"\\;X:\\srv\share\mscordaccore.dll")]
        [InlineData(@"\\srv@SSL\DavWWWRoot\mscordaccore.dll")]
        public void IsSafeAbsoluteLocalPathRejectsRemoteOrDevicePaths(string path)
        {
            Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("mscordaccore.dll")]
        [InlineData(@"sub\mscordaccore.dll")]
        [InlineData("sub/mscordaccore.dll")]
        [InlineData(@"..\mscordaccore.dll")]
        [InlineData("../mscordaccore.dll")]
        [InlineData(@".\mscordaccore.dll")]
        public void IsSafeAbsoluteLocalPathRejectsEmptyOrRelativePaths(string path)
        {
            Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(path));
        }

        [Fact]
        public void IsSafeAbsoluteLocalPathAcceptsOnlyHostLocalAbsolutePaths()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath(@"C:\Windows\mscordaccore.dll"));
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath("C:/Windows/mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"C:mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath("C:"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"1:\mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"\mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath("/mscordaccore.dll"));
            }
            else
            {
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath("/usr/share/dotnet/libmscordaccore.so"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"C:\Windows\mscordaccore.dll"));
            }
        }

        [Theory]
        [InlineData(@"\\remote.example\share\mscordaccore.dll", "mscordaccore.dll")]
        [InlineData("//remote.example/share/mscordaccore.dll", "mscordaccore.dll")]
        [InlineData(@"C:\Windows\mscordaccore.dll", "mscordaccore.dll")]
        [InlineData("/usr/share/dotnet/libmscordaccore.so", "libmscordaccore.so")]
        [InlineData(@"a\b/libmscordbi.so", "libmscordbi.so")]
        [InlineData("mscordaccore.dll", "mscordaccore.dll")]
        [InlineData(@"dir\", "")]
        [InlineData("dir/", "")]
        public void GetFileNameStripsBothDirectorySeparators(string path, string expected)
        {
            Assert.Equal(expected, PathUtilities.GetFileName(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GetFileNamePassesThroughNullOrEmptyPaths(string path)
        {
            Assert.Equal(path, PathUtilities.GetFileName(path));
        }

        [Theory]
        [InlineData(DebugLibraryKind.Dac)]
        [InlineData(DebugLibraryKind.Dbi)]
        [InlineData(DebugLibraryKind.CDac)]
        public void RuntimeRejectsRemoteLibraryPaths(DebugLibraryKind kind)
        {
            string candidate = Microsoft.Diagnostics.DebugServices.Implementation.Runtime.GetLocalCandidatePath(
                kind,
                @"\\remote.example\share\mscordaccore.dll",
                null,
                @"\\remote.example\share\coreclr.dll");

            Assert.Null(candidate);
        }

        [Fact]
        public void RuntimeRejectsRemoteRuntimeDirectoryOverride()
        {
            string runtimeModulePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? @"C:\dotnet\coreclr.dll"
                : "/usr/share/dotnet/libcoreclr.so";

            string candidate = Microsoft.Diagnostics.DebugServices.Implementation.Runtime.GetLocalCandidatePath(
                DebugLibraryKind.Dac,
                "mscordaccore.dll",
                @"\\remote.example\share",
                runtimeModulePath);

            Assert.Null(candidate);
        }

        [Fact]
        public void SymbolServiceRejectsAdjacentPdbProbeForRemoteAssembly()
        {
            string pdbPath = SymbolService.GetLocalPdbPath(
                @"\\remote.example\share\module.dll",
                @"\\remote.example\share\module.pdb");

            Assert.Null(pdbPath);
        }
    }
}
