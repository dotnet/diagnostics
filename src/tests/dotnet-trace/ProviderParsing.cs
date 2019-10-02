// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class ProviderParsingTests
    {
        [Theory]
        [InlineData("VeryCoolProvider:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        [InlineData("VeryCoolProvider:1:5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        public void ValidProvider_CorrectlyParses(string providerToParse)
        {
            List<Provider> parsedProviders = Extensions.ToProviders(providerToParse);
            Assert.True(parsedProviders.Count == 1);
            Provider provider = parsedProviders.First();
            Assert.True(provider.Name == "VeryCoolProvider");
            Assert.True(provider.Keywords == 1);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Verbose);
            Assert.True(provider.FilterData == "FilterAndPayloadSpecs=\"QuotedValue\"");
        }

        [Theory]
        [InlineData("VeryCoolProvider:0x1:5:FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value\"")]
        public void ValidProviderFilter_CorrectlyParses(string providerToParse)
        {
            List<Provider> parsedProviders = Extensions.ToProviders(providerToParse);
            Assert.True(parsedProviders.Count == 1);
            Provider provider = parsedProviders.First();
            Assert.True(provider.Name == "VeryCoolProvider");
            Assert.True(provider.Keywords == 1);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Verbose);
            Assert.True(provider.FilterData == "FilterAndPayloadSpecs=\"QuotedValue:-\r\n\"");
        }

        [Theory]
        [InlineData(null)]
        [InlineData(",")]
        public void EmptyProvider_CorrectlyThrows(string providerToParse)
        {
            Assert.Throws<ArgumentNullException>(() => Extensions.ToProviders(providerToParse));
        }

        [Theory]
        [InlineData(":::")]
        [InlineData(":1:1")]
        public void InvalidProvider_CorrectlyThrows(string providerToParse)
        {
            Assert.Throws<ArgumentException>(() => Extensions.ToProviders(providerToParse));
        }

        [Theory]
        [InlineData("VeryCoolProvider:0xFFFFFFFFFFFFFFFF:5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        [InlineData("VeryCoolProvider::5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        public void ValidProviderKeyword_CorrectlyParses(string providerToParse)
        {
            List<Provider> parsedProviders = Extensions.ToProviders(providerToParse);
            Assert.True(parsedProviders.Count == 1);
            Provider provider = parsedProviders.First();
            Assert.True(provider.Name == "VeryCoolProvider");
            Assert.True(provider.Keywords == ulong.MaxValue);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Verbose);
            Assert.True(provider.FilterData == "FilterAndPayloadSpecs=\"QuotedValue\"");
        }

        [Theory]
        [InlineData("VeryCoolProvider::5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        [InlineData("VeryCoolProvider:::FilterAndPayloadSpecs=\"QuotedValue\"")]
        public void ValidProviderEventLevel_CorrectlyParses(string providerToParse)
        {
            List<Provider> parsedProviders = Extensions.ToProviders(providerToParse);
            Assert.True(parsedProviders.Count == 1);
            Provider provider = parsedProviders.First();
            Assert.True(provider.Name == "VeryCoolProvider");
            Assert.True(provider.Keywords == ulong.MaxValue);
            Assert.True(provider.EventLevel == System.Diagnostics.Tracing.EventLevel.Verbose);
            Assert.True(provider.FilterData == "FilterAndPayloadSpecs=\"QuotedValue\"");
        }

        [Theory]
        [InlineData("VeryCoolProvider:0xFFFFFFFFFFFFFFFFF:5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        [InlineData("VeryCoolProvider:0x10000000000000000::FilterAndPayloadSpecs=\"QuotedValue\"")]
        public void OutOfRangekeyword_CorrectlyThrows(string providerToParse)
        {
            Assert.Throws<OverflowException>(() => Extensions.ToProviders(providerToParse));
        }

        [Theory]
        [InlineData("VeryCoolProvider:--:5:FilterAndPayloadSpecs=\"QuotedValue\"")]
        [InlineData("VeryCoolProvider:gh::FilterAndPayloadSpecs=\"QuotedValue\"")]
        public void Invalidkeyword_CorrectlyThrows(string providerToParse)
        {
            Assert.Throws<FormatException>(() => Extensions.ToProviders(providerToParse));
        }

        [Theory]
        [InlineData("ProviderOne:0x1:1:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:2:2:key=value,ProviderThree:3:3:key=value")]
        [InlineData("ProviderOne:1:1:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:0x2:2:key=value,ProviderThree:0x3:3:key=value")]
        public void MultipleValidProviders_CorrectlyParses(string providersToParse)
        {
            List<Provider> parsedProviders = Extensions.ToProviders(providersToParse);
            Assert.True(parsedProviders.Count == 3);
            Provider providerOne = parsedProviders[0];
            Provider providerTwo = parsedProviders[1];
            Provider providerThree = parsedProviders[2];

            Assert.True(providerOne.Name == "ProviderOne");
            Assert.True(providerOne.Keywords == 1);
            Assert.True(providerOne.EventLevel == System.Diagnostics.Tracing.EventLevel.Critical);
            Assert.True(providerOne.FilterData == "FilterAndPayloadSpecs=\"QuotedValue\"");

            Assert.True(providerTwo.Name == "ProviderTwo");
            Assert.True(providerTwo.Keywords == 2);
            Assert.True(providerTwo.EventLevel == System.Diagnostics.Tracing.EventLevel.Error);
            Assert.True(providerTwo.FilterData == "key=value");

            Assert.True(providerThree.Name == "ProviderThree");
            Assert.True(providerThree.Keywords == 3);
            Assert.True(providerThree.EventLevel == System.Diagnostics.Tracing.EventLevel.Warning);
            Assert.True(providerThree.FilterData == "key=value");
        }

        [Theory]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",:2:2:key=value,ProviderThree:3:3:key=value")]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:2:2:key=value,:3:3:key=value")]
        [InlineData("ProviderOne:0x1:5:key=value,key=FilterAndPayloadSpecs=\"QuotedValue\",:2:2:key=value,ProviderThree:3:3:key=value")]
        public void MultipleValidProvidersWithOneInvalidProvider_CorrectlyThrows(string providersToParse)
        {
            Assert.Throws<ArgumentException>(() => Extensions.ToProviders(providersToParse));
        }

        [Theory]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:0xFFFFFFFFFFFFFFFFF:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value")]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:0x10000000000000000:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value")]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:-1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value")]
        public void MultipleValidProvidersWithOneOutOfRangeKeyword_CorrectlyThrows(string providersToParse)
        {
            Assert.Throws<OverflowException>(() => Extensions.ToProviders(providersToParse));
        }

        [Theory]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:--:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value")]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:gh:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value")]
        [InlineData("ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:$:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value")]
        public void MultipleValidProvidersWithOneInvalidKeyword_CorrectlyThrows(string providersToParse)
        {
            Assert.Throws<FormatException>(() => Extensions.ToProviders(providersToParse));
        }

        [Theory]
        [InlineData("ProviderOne:0x1:1:FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value:-A=B;C=D;\",ProviderTwo:2:2:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value:-A=B;C=D;\"")]
        public void MultipleProvidersWithComplexFilters_CorrectlyParse(string providersToParse)
        {
            List<Provider> parsedProviders = Extensions.ToProviders(providersToParse);
            Assert.True(parsedProviders.Count == 3);
            Provider providerOne = parsedProviders[0];
            Provider providerTwo = parsedProviders[1];
            Provider providerThree = parsedProviders[2];

            Assert.True(providerOne.Name == "ProviderOne");
            Assert.True(providerOne.Keywords == 1);
            Assert.True(providerOne.EventLevel == System.Diagnostics.Tracing.EventLevel.Critical);
            Assert.True(providerOne.FilterData == "FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value:-A=B;C=D;\"");

            Assert.True(providerTwo.Name == "ProviderTwo");
            Assert.True(providerTwo.Keywords == 2);
            Assert.True(providerTwo.EventLevel == System.Diagnostics.Tracing.EventLevel.Error);
            Assert.True(providerTwo.FilterData == "FilterAndPayloadSpecs=\"QuotedValue\"");

            Assert.True(providerThree.Name == "ProviderThree");
            Assert.True(providerThree.Keywords == 3);
            Assert.True(providerThree.EventLevel == System.Diagnostics.Tracing.EventLevel.Warning);
            Assert.True(providerThree.FilterData == "FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value:-A=B;C=D;\"");
        }
    }
}