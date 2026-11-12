// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
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
                string localPath = Path.Combine(Environment.SystemDirectory, "mscordaccore.dll");
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath(localPath));
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath(localPath.Replace('\\', '/')));
                // Drive-letter mapped network shares (for example Azure Files mounted as Z:) stay supported.
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath(@"Z:\mounted\mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"C:mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath("C:"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"1:\mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"\mscordaccore.dll"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath("/mscordaccore.dll"));
            }
            else
            {
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath("/usr/share/dotnet/libmscordaccore.so"));
                // Network mounts such as /mnt/azurefiles stay supported.
                Assert.True(PathUtilities.IsSafeAbsoluteLocalPath("/mnt/share/libmscordaccore.so"));
                Assert.False(PathUtilities.IsSafeAbsoluteLocalPath(@"C:\Windows\mscordaccore.dll"));
            }
        }

        [Theory]
        [InlineData(@"\\remote.example\share\mscordaccore.dll", "mscordaccore.dll")]
        [InlineData("//remote.example/share/mscordaccore.dll", "mscordaccore.dll")]
        [InlineData(@"C:\Windows\mscordaccore.dll", "mscordaccore.dll")]
        [InlineData("/usr/share/dotnet/libmscordaccore.so", "libmscordaccore.so")]
        [InlineData(@"a\b/libmscordbi.so", "libmscordbi.so")]
        [InlineData("Z:coreclr.dll", "coreclr.dll")]
        [InlineData("Z:Z:coreclr.dll", "coreclr.dll")]
        [InlineData(@"remote\Z:module.pdb", "module.pdb")]
        [InlineData("mscordaccore.dll", "mscordaccore.dll")]
        [InlineData(@"dir\", "")]
        [InlineData("dir/", "")]
        public void GetFileNameStripsDirectoriesAndDriveQualifiers(string path, string expected)
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
    }
}
