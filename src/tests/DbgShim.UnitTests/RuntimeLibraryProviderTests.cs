// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;
using Xunit;

namespace DbgShim.UnitTests
{
    public class RuntimeLibraryProviderTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "RuntimeLibraryProviderTests-" + Path.GetRandomFileName());

        public RuntimeLibraryProviderTests()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Path.Combine(_directory, "mscordaccore.dll"), [0]);
            File.WriteAllBytes(Path.Combine(_directory, "mscordbi.dll"), [0]);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Theory]
        [InlineData("mscordaccore.dll")]
        [InlineData("mscordbi.dll")]
        public void ProvideLibraryVerifiesDacAndDbiAlike(string fileName)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Skip("Authenticode verification is Windows-only");
            }

            // Only the debugger-bundled mscordbi_universal (loaded by dbgshim) skips verification.
            // A runtime DBI is verified exactly like the DAC, so an unsigned one must be refused.
            using RuntimeLibraryProvider provider = new(
                () => Path.Combine(_directory, "mscordbi.dll"),
                () => Path.Combine(_directory, "mscordaccore.dll"),
                verifySignature: true);

            int result = provider.ProvideLibrary2(IntPtr.Zero, fileName, timeStamp: 0, sizeOfImage: 0, out IntPtr modulePath);

            Assert.Equal(HResult.E_NOINTERFACE, result);
            Assert.Equal(IntPtr.Zero, modulePath);
        }

        [Fact]
        public void DesktopDbiLoadVerifiesSignature()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Skip("Authenticode verification is Windows-only");
            }

            Assert.Equal(IntPtr.Zero, RuntimeWrapper.LoadLibraryWithSignatureVerification(Path.Combine(_directory, "mscordbi.dll"), verifySignature: true, out VerifiedLibraryCopy libraryCopy));
            Assert.Null(libraryCopy);
        }

        [Fact]
        public void ProvideLibraryReturnsVerifiedPrivateCopy()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Skip("Authenticode verification is Windows-only");
            }
            string desktopDacPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", Environment.Is64BitProcess ? "Framework64" : "Framework", "v4.0.30319", "mscordacwks.dll");
            if (!File.Exists(desktopDacPath))
            {
                Assert.Skip($"Desktop DAC not available: {desktopDacPath}");
            }
            if (!AuthenticodeUtil.VerifyDacDll(desktopDacPath, out IDisposable fileLock))
            {
                Assert.Skip($"Desktop DAC is not signed: {desktopDacPath}");
            }
            fileLock.Dispose();

            string copyPath;
            using (RuntimeLibraryProvider provider = new(() => null, () => desktopDacPath, verifySignature: true))
            {
                int result = provider.ProvideLibrary2(IntPtr.Zero, "mscordacwks.dll", timeStamp: 0, sizeOfImage: 0, out IntPtr modulePath);
                try
                {
                    Assert.Equal(HResult.S_OK, result);
                    copyPath = Marshal.PtrToStringUni(modulePath);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(modulePath);
                }

                // The returned path is a private copy, not the original, and it still verifies.
                Assert.NotEqual(Path.GetDirectoryName(desktopDacPath), Path.GetDirectoryName(copyPath), StringComparer.OrdinalIgnoreCase);
                Assert.Equal("mscordacwks.dll", Path.GetFileName(copyPath), ignoreCase: true);
                Assert.True(File.Exists(copyPath));
            }

            // Nothing loaded the copy, so disposing the provider deletes it.
            Assert.False(File.Exists(copyPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(copyPath)));
        }

        [Theory]
        [InlineData("mscordaccore.dll")]
        [InlineData("mscordbi.dll")]
        public void ProvideLibraryReturnsLocalPath(string fileName)
        {
            using RuntimeLibraryProvider provider = new(
                () => Path.Combine(_directory, "mscordbi.dll"),
                () => Path.Combine(_directory, "mscordaccore.dll"),
                verifySignature: false);

            int result = provider.ProvideLibrary2(IntPtr.Zero, fileName, timeStamp: 0, sizeOfImage: 0, out IntPtr modulePath);

            try
            {
                Assert.Equal(HResult.S_OK, result);
                Assert.Equal(Path.Combine(_directory, fileName), Marshal.PtrToStringUni(modulePath));
            }
            finally
            {
                Marshal.FreeCoTaskMem(modulePath);
            }
        }
    }
}
