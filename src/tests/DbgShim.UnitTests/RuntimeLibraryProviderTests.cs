// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;
using Xunit;

namespace DbgShim.UnitTests
{
    public class RuntimeLibraryProviderTests
    {
        [Theory]
        [InlineData("mscordaccore.dll")]
        [InlineData("mscordbi.dll")]
        public void ProvideLibraryRejectsRemotePath(string fileName)
        {
            using RuntimeLibraryProvider provider = new(
                () => @"\\remote.example\share\mscordbi.dll",
                () => @"\\remote.example\share\mscordaccore.dll",
                verifySignature: false);

            int result = provider.ProvideLibrary2(
                IntPtr.Zero,
                fileName,
                timeStamp: 0,
                sizeOfImage: 0,
                out IntPtr modulePath);

            Assert.Equal(HResult.E_NOINTERFACE, result);
            Assert.Equal(IntPtr.Zero, modulePath);
        }
    }
}
