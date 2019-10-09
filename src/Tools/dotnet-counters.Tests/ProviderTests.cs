using System;
using Xunit;
using Microsoft.Diagnostics.Tools.Counters;

namespace dotnet_counters.Tests
{
    /// <summary>
    /// This is a suite of tests that test the following:
    /// 1) Provider string build validation
    /// 2) Getting the right set of profiles
    /// </summary>
    public class ProviderTests
    {
        public ProviderTests()
        {

        }

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
        public void UnknwonProvider()
        {
            KnownData.TryGetProvider("SomeRandomProvider", out CounterProvider randomProvider);

            Assert.Null(randomProvider);
        }

        // TODO: Add more as we add more providers as known providers to the tool...
    }
}
