// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Microsoft.Diagnostics.Tools.Counters
{
    /// <summary>
    /// These test the some of the known providers that we provide as a default configuration for customers to use.
    /// </summary>
    public class KnownProviderTests
    {
        [Fact]
        public void TestRuntimeProvider()
        {
            KnownData.TryGetProvider("System.Runtime", out CounterProvider runtimeProvider);

            Assert.Equal("System.Runtime", runtimeProvider.Name);
            Assert.Equal("0xffffffff", runtimeProvider.Keywords);
            Assert.Equal("5", runtimeProvider.Level);
            Assert.Equal("System.Runtime:0xffffffff:5:EventCounterIntervalSec=1", runtimeProvider.ToProviderString(1));
        }

        [Fact]
        public void TestASPNETProvider()
        {
            KnownData.TryGetProvider("Microsoft.AspNetCore.Hosting", out CounterProvider aspnetProvider);

            Assert.Equal("Microsoft.AspNetCore.Hosting", aspnetProvider.Name);
            Assert.Equal("0x0", aspnetProvider.Keywords);
            Assert.Equal("4", aspnetProvider.Level);
            Assert.Equal("Microsoft.AspNetCore.Hosting:0x0:4:EventCounterIntervalSec=5", aspnetProvider.ToProviderString(5));
        }

        [Fact]
        public void UnknownProvider()
        {
            KnownData.TryGetProvider("SomeRandomProvider", out CounterProvider randomProvider);

            Assert.Null(randomProvider);
        }

        // TODO: Add more as we add more providers as known providers to the tool...
    }
}
