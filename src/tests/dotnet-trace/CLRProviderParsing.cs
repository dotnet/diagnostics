// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class CLRProviderParsingTests
    {
        private static string CLRProviderName = "Microsoft-Windows-DotNETRuntime";

        [Theory]
        [InlineData("gc")]
        [InlineData("Gc")]
        [InlineData("GC")]
        public void ValidSingleCLREvent(string providerToParse)
        {
            NETCore.Client.EventPipeProvider provider = Extensions.ToCLREventPipeProvider(providerToParse, "4");
            Assert.True(provider.Name == CLRProviderName);
            Assert.True(provider.Keywords == 1);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Informational);
            Assert.True(provider.Arguments == null);
        }

        [Theory]
        [InlineData("nosuchevent")]
        [InlineData("something")]
        [InlineData("haha")]
        public void InValidSingleCLREvent(string providerToParse)
        {
            Assert.Throws<ArgumentException>(() => Extensions.ToCLREventPipeProvider(providerToParse, "4"));
        }

        [Theory]
        [InlineData("gc+gchandle")]
        [InlineData("gc+GCHandle")]
        [InlineData("GC+GCHandle")]
        public void ValidManyCLREvents(string providerToParse)
        {
            NETCore.Client.EventPipeProvider provider = Extensions.ToCLREventPipeProvider(providerToParse, "5");
            Assert.True(provider.Name == CLRProviderName);
            Assert.True(provider.Keywords == 3);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Verbose);
            Assert.True(provider.Arguments == null);
        }

        [Theory]
        [InlineData("informational")]
        [InlineData("4")]
        [InlineData("Informational")]
        [InlineData("InFORMationAL")]
        public void ValidCLREventLevel(string clreventlevel)
        {
            NETCore.Client.EventPipeProvider provider = Extensions.ToCLREventPipeProvider("gc", clreventlevel);
            Assert.True(provider.Name == CLRProviderName);
            Assert.True(provider.Keywords == 1);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Informational);
            Assert.True(provider.Arguments == null);
        }

        [Theory]
        [InlineData("something")]
        [InlineData("hello")]
        public void InvalidCLREventLevel(string clreventlevel)
        {
            Assert.Throws<ArgumentException>(() => Extensions.ToCLREventPipeProvider("gc", clreventlevel));
        }
    }
}
