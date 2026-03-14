// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class ProviderCompositionTests
    {
        private static readonly Dictionary<string, string> simpleArgs = new() { { "FilterAndPayloadSpecs", "QuotedValue" } };
        private static readonly Dictionary<string, string> keyValueArgs = new() { { "key", "value" } };
        private static readonly Dictionary<string, string> complexArgs = new() { { "FilterAndPayloadSpecs", "QuotedValue:-\r\nQuoted/Value" } };
        private static readonly Dictionary<string, string> complexABCDArgs = new() { { "FilterAndPayloadSpecs", "QuotedValue:-\r\nQuoted/Value:-A=B;C=D;" } };

        public static IEnumerable<object[]> ValidProviders()
        {
            yield return new object[] { "VeryCoolProvider:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\"", new EventPipeProvider("VeryCoolProvider", EventLevel.Verbose, 0x1, simpleArgs) };
            yield return new object[] { "VeryCoolProvider:1:5:FilterAndPayloadSpecs=\"QuotedValue\"",   new EventPipeProvider("VeryCoolProvider", EventLevel.Verbose, 0x1, simpleArgs) };
            yield return new object[] { "VeryCoolProvider:0x1:5:FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value\"", new EventPipeProvider("VeryCoolProvider", EventLevel.Verbose, 0x1, complexArgs) };
            yield return new object[] { "VeryCoolProvider:0xFFFFFFFFFFFFFFFF:5:FilterAndPayloadSpecs=\"QuotedValue\"", new EventPipeProvider("VeryCoolProvider", EventLevel.Verbose, unchecked((long)0xFFFFFFFFFFFFFFFF), simpleArgs) };
            yield return new object[] { "VeryCoolProvider::4:FilterAndPayloadSpecs=\"QuotedValue\"",  new EventPipeProvider("VeryCoolProvider", EventLevel.Informational, 0, simpleArgs) };
            yield return new object[] { "VeryCoolProvider:::FilterAndPayloadSpecs=\"QuotedValue\"",    new EventPipeProvider("VeryCoolProvider", EventLevel.Informational, 0, simpleArgs) };
            yield return new object[] { "ProviderOne:0x1:Verbose", new EventPipeProvider("ProviderOne", EventLevel.Verbose, 0x1) };
            yield return new object[] { "ProviderOne:0x1:verbose", new EventPipeProvider("ProviderOne", EventLevel.Verbose, 0x1) };
            yield return new object[] { "ProviderOne:0x1:Informational", new EventPipeProvider("ProviderOne", EventLevel.Informational, 0x1) };
            yield return new object[] { "ProviderOne:0x1:INFORMATIONAL", new EventPipeProvider("ProviderOne", EventLevel.Informational, 0x1) };
            yield return new object[] { "ProviderOne:0x1:LogAlways", new EventPipeProvider("ProviderOne", EventLevel.LogAlways, 0x1) };
            yield return new object[] { "ProviderOne:0x1:LogAlwayS", new EventPipeProvider("ProviderOne", EventLevel.LogAlways, 0x1) };
            yield return new object[] { "ProviderOne:0x1:Error", new EventPipeProvider("ProviderOne", EventLevel.Error, 0x1) };
            yield return new object[] { "ProviderOne:0x1:ERRor", new EventPipeProvider("ProviderOne", EventLevel.Error, 0x1) };
            yield return new object[] { "ProviderOne:0x1:Critical", new EventPipeProvider("ProviderOne", EventLevel.Critical, 0x1) };
            yield return new object[] { "ProviderOne:0x1:CRITICAL", new EventPipeProvider("ProviderOne", EventLevel.Critical, 0x1) };
            yield return new object[] { "ProviderOne:0x1:Warning", new EventPipeProvider("ProviderOne", EventLevel.Warning, 0x1) };
            yield return new object[] { "ProviderOne:0x1:warning", new EventPipeProvider("ProviderOne", EventLevel.Warning, 0x1) };
            yield return new object[] { "MyProvider:::A=B;C=D", new EventPipeProvider("MyProvider", EventLevel.Informational, 0x0, new Dictionary<string, string> { { "A", "B" }, { "C", "D" } }) };
        }

        public static IEnumerable<object[]> InvalidProviders()
        {
            yield return new object[] { ":::", typeof(DiagnosticToolException) };
            yield return new object[] { ":1:1", typeof(DiagnosticToolException) };
            yield return new object[] { "ProviderOne:0x1:UnknownLevel", typeof(DiagnosticToolException) };
            yield return new object[] { "VeryCoolProvider:0x0:-1", typeof(DiagnosticToolException) };
            yield return new object[] { "VeryCoolProvider:0xFFFFFFFFFFFFFFFFF:5:FilterAndPayloadSpecs=\"QuotedValue\"", typeof(OverflowException) };
            yield return new object[] { "VeryCoolProvider:0x10000000000000000::FilterAndPayloadSpecs=\"QuotedValue\"", typeof(OverflowException) };
            yield return new object[] { "VeryCoolProvider:__:5:FilterAndPayloadSpecs=\"QuotedValue\"", typeof(FormatException) };
            yield return new object[] { "VeryCoolProvider:gh::FilterAndPayloadSpecs=\"QuotedValue\"", typeof(FormatException) };
        }

        [Theory]
        [MemberData(nameof(ValidProviders))]
        public void ProvidersArg_ParsesCorrectly(string providersArg, EventPipeProvider expected)
        {
            string[] providers = providersArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            List<EventPipeProvider> parsedProviders = ProviderUtils.ComputeProviderConfig(providers, string.Empty, string.Empty, Array.Empty<string>());
            EventPipeProvider actual = Assert.Single(parsedProviders);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(InvalidProviders))]
        public void InvalidProvidersArg_Throws(string providersArg, Type expectedException)
        {
            string[] providers = providersArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Assert.Throws(expectedException, () => ProviderUtils.ComputeProviderConfig(providers, string.Empty, string.Empty, Array.Empty<string>()));
        }

        public static IEnumerable<object[]> MultipleValidProviders()
        {
            yield return new object[] {
                "ProviderOne:0x1:1:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:2:2:key=value,ProviderThree:3:3:key=value",
                new[] {
                    new EventPipeProvider("ProviderOne", EventLevel.Critical, 0x1, simpleArgs),
                    new EventPipeProvider("ProviderTwo", EventLevel.Error, 0x2, keyValueArgs),
                    new EventPipeProvider("ProviderThree", EventLevel.Warning, 0x3, keyValueArgs)
                }
            };
            yield return new object[] {
                "ProviderOne:0x1:1:FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value:-A=B;C=D;\",ProviderTwo:2:2:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:FilterAndPayloadSpecs=\"QuotedValue:-\r\nQuoted/Value:-A=B;C=D;\"",
                new[] {
                    new EventPipeProvider("ProviderOne", EventLevel.Critical, 0x1, complexABCDArgs),
                    new EventPipeProvider("ProviderTwo", EventLevel.Error, 0x2, simpleArgs),
                    new EventPipeProvider("ProviderThree", EventLevel.Warning, 0x3, complexABCDArgs)
                }
            };
            yield return new object[] {
                "MyProvider:::A=B;C=\"D\",MyProvider2:::A=1;B=2;",
                new[] {
                    new EventPipeProvider("MyProvider", EventLevel.Informational, 0x0, new Dictionary<string, string> { { "A", "B" }, { "C", "D" } }),
                    new EventPipeProvider("MyProvider2", EventLevel.Informational, 0x0, new Dictionary<string, string> { { "A", "1" }, { "B", "2" } })
                }
            };
            yield return new object[] {
                "MyProvider:::A=\"B;C=D\",MyProvider2:::A=\"spaced words\";C=1285;D=Spaced Words 2",
                new[] {
                    new EventPipeProvider("MyProvider", EventLevel.Informational, 0x0, new Dictionary<string, string> { { "A", "B;C=D" } }),
                    new EventPipeProvider("MyProvider2", EventLevel.Informational, 0x0, new Dictionary<string, string> { { "A", "spaced words" }, { "C", "1285" }, { "D", "Spaced Words 2" } })
                }
            };
        }

        [Theory]
        [MemberData(nameof(MultipleValidProviders))]
        public void MultipleProviders_Parse_AsExpected(string providersArg, EventPipeProvider[] expected)
        {
            string[] providers = providersArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            List<EventPipeProvider> parsed = ProviderUtils.ComputeProviderConfig(providers, string.Empty, string.Empty, Array.Empty<string>());
            Assert.Equal(expected.Length, parsed.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], parsed[i]);
            }
        }

        public static IEnumerable<object[]> MultipleInvalidProviders()
        {
            yield return new object[] { "ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",:2:2:key=value,ProviderThree:3:3:key=value", typeof(DiagnosticToolException) };
            yield return new object[] { "ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:0xFFFFFFFFFFFFFFFFF:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value", typeof(OverflowException) };
            yield return new object[] { "ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:0x10000000000000000:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value", typeof(OverflowException) };
            yield return new object[] { "ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:18446744073709551615:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value", typeof(OverflowException) };
            yield return new object[] { "ProviderOne:0x1:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderTwo:__:5:FilterAndPayloadSpecs=\"QuotedValue\",ProviderThree:3:3:key=value", typeof(FormatException) };
        }

        [Theory]
        [MemberData(nameof(MultipleInvalidProviders))]
        public void MultipleProviders_FailureCases_Throw(string providersArg, Type expectedException)
        {
            string[] providers = providersArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Assert.Throws(expectedException, () => ProviderUtils.ComputeProviderConfig(providers, string.Empty, string.Empty, Array.Empty<string>()));
        }

        public static IEnumerable<object[]> DedupeSuccessCases()
        {
            yield return new object[] { new[]{ "DupeProvider", "DupeProvider:0xF:LogAlways" }, new EventPipeProvider("DupeProvider", EventLevel.LogAlways, 0xF) };
            yield return new object[] { new[]{ "DupeProvider:0xF0:Informational", "DupeProvider:0xF:Verbose" }, new EventPipeProvider("DupeProvider", EventLevel.Verbose, 0xFF) };
            yield return new object[] { new[]{ "MyProvider:0x1:Informational", "MyProvider:0x2:Verbose" }, new EventPipeProvider("MyProvider", EventLevel.Verbose, 0x3) };
            yield return new object[] { new[] { "MyProvider:0x1:5", "MyProvider:0x2:LogAlways" }, new EventPipeProvider("MyProvider", EventLevel.LogAlways, 0x3) };
            yield return new object[] { new[]{ "MyProvider:0x1:Error", "myprovider:0x2:Critical" }, new EventPipeProvider("MyProvider", EventLevel.Error, 0x3) };
        }

        public static IEnumerable<object[]> DedupeFailureCases()
        {
            yield return new object[] { new[]{ "MyProvider:::key=value", "MyProvider:::key=value" }, typeof(DiagnosticToolException) };
        }

        [Theory]
        [MemberData(nameof(DedupeSuccessCases))]
        public void DedupeProviders_Success(string[] providersArg, EventPipeProvider expected)
        {
            List<EventPipeProvider> list = ProviderUtils.ComputeProviderConfig(providersArg, string.Empty, string.Empty, Array.Empty<string>());
            EventPipeProvider actual = Assert.Single(list);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(DedupeFailureCases))]
        public void DedupeProviders_Failure(string[] providersArg, Type expectedException)
        {
            Assert.Throws(expectedException, () => ProviderUtils.ComputeProviderConfig(providersArg, string.Empty, string.Empty, Array.Empty<string>()));
        }

        public static IEnumerable<object[]> PrecedenceCases()
        {
            yield return new object[] {
                Array.Empty<string>(),
                "gc+jit",
                string.Empty,
                Array.Empty<string>(),
                new[]{ new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x1 | 0x10) }
            };

            yield return new object[] {
                Array.Empty<string>(),
                "gc",
                "Verbose",
                new[]{ "dotnet-common", "dotnet-sampled-thread-time" },
                new[]{
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x100003801D),
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, 0xF00000000000)
                }
            };

            yield return new object[] {
                new[]{ "Microsoft-Windows-DotNETRuntime:0x40000000:Verbose" },
                "gc",
                "Informational",
                new[]{ "dotnet-common" },
                new[]{ new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x40000000) }
            };

            yield return new object[] {
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                new[]{ "dotnet-common" },
                new[]{ new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x100003801D) }
            };

            yield return new object[] {
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                new[]{ "dotnet-common", "gc-verbose" },
                new[]{ new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x100003801D) }
            };

            yield return new object[] {
                new[]{ "Microsoft-Windows-DotNETRuntime:0x0:Informational" },
                string.Empty,
                string.Empty,
                new[]{ "dotnet-common" },
                new[]{ new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x0) }
            };
        }

        [Theory]
        [MemberData(nameof(PrecedenceCases))]
        public void ProviderSourcePrecedence(string[] providersArg, string clreventsArg, string clreventLevel, string[] profiles, EventPipeProvider[] expected)
        {
            List<EventPipeProvider> actual = ProviderUtils.ComputeProviderConfig(providersArg, clreventsArg, clreventLevel, profiles);
            Assert.Equal(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }

        public static IEnumerable<object[]> InvalidClrEvents()
        {
            yield return new object[] { Array.Empty<string>(), "gc+bogus", string.Empty, Array.Empty<string>(), typeof(DiagnosticToolException) };
        }

        [Theory]
        [MemberData(nameof(InvalidClrEvents))]
        public void UnknownClrEvents_Throws(string[] providersArg, string clreventsArg, string clreventLevel, string[] profiles, Type expectedException)
        {
            Assert.Throws(expectedException, () => ProviderUtils.ComputeProviderConfig(providersArg, clreventsArg, clreventLevel, profiles));
        }

        public record ProviderSourceExpectation(string Name, bool FromProviders, bool FromClrEvents, bool FromProfile);

        public static IEnumerable<object[]> ProviderSourcePrintCases()
        {
            yield return new object[] {
                new[]{ "MyProvider:0x1:Error" },
                "gc",
                "Informational",
                new[]{ "dotnet-sampled-thread-time" },
                new[]{
                    new ProviderSourceExpectation("MyProvider", true, false, false),
                    new ProviderSourceExpectation("Microsoft-Windows-DotNETRuntime", false, true, false),
                    new ProviderSourceExpectation("Microsoft-DotNETCore-SampleProfiler", false, false, true)
                }
            };
        }

        [Theory]
        [MemberData(nameof(ProviderSourcePrintCases))]
        public void PrintProviders_Sources(string[] providersArg, string clreventsArg, string clreventLevel, string[] profiles, ProviderSourceExpectation[] expectations)
        {
            StringWriter capture = new();
            TextWriter original = Console.Out;
            try
            {
                Console.SetOut(capture);
                _ = ProviderUtils.ComputeProviderConfig(providersArg, clreventsArg, clreventLevel, profiles, true);
                string output = capture.ToString();
                foreach (ProviderSourceExpectation e in expectations)
                {
                    string line = output.WhereLineContains(e.Name);
                    Assert.Equal(e.FromProviders, line.Contains("--providers", StringComparison.Ordinal));
                    Assert.Equal(e.FromClrEvents, line.Contains("--clrevents", StringComparison.Ordinal));
                    Assert.Equal(e.FromProfile, line.Contains("--profile", StringComparison.Ordinal));
                }
            }
            finally
            {
                Console.SetOut(original);
            }
        }

        public static IEnumerable<object[]> MergingCases()
        {
            yield return new object[] { new[]{ "MyProvider:0x1:5", "MyProvider:0x2:LogAlways" }, string.Empty, string.Empty, Array.Empty<string>(), new EventPipeProvider("MyProvider", EventLevel.LogAlways, 0x3) };
            yield return new object[] { new[]{ "MyProvider:0x1:Error", "myprovider:0x2:Critical" }, string.Empty, string.Empty, Array.Empty<string>(), new EventPipeProvider("MyProvider", EventLevel.Error, 0x3) };
        }

        [Theory]
        [MemberData(nameof(MergingCases))]
        public void MergeDuplicateProviders(string[] providersArg, string clreventsArg, string clreventLevel, string[] profiles, EventPipeProvider expected)
        {
            List<EventPipeProvider> actual = ProviderUtils.ComputeProviderConfig(providersArg, clreventsArg, clreventLevel, profiles);
            EventPipeProvider single = Assert.Single(actual);
            Assert.Equal(expected, single);
        }

        [Theory]
        [InlineData("MyProvider:0x0:9", EventLevel.Verbose)]
        public void ProviderEventLevel_Clamps(string providersArg, EventLevel expected)
        {
            string[] providers = providersArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            EventPipeProvider actual = Assert.Single(ProviderUtils.ComputeProviderConfig(providers, string.Empty, string.Empty, Array.Empty<string>()));
            Assert.Equal(expected, actual.EventLevel);
        }

        public static IEnumerable<object[]> ClrEventLevelCases()
        {
            yield return new object[] { Array.Empty<string>(), "gc+jit", "5", Array.Empty<string>(), new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x1 | 0x10) };
        }

        [Theory]
        [MemberData(nameof(ClrEventLevelCases))]
        public void CLREvents_NumericLevel_Parses(string[] providersArg, string clreventsArg, string clreventLevel, string[] profiles, EventPipeProvider expected)
        {
            List<EventPipeProvider> actual = ProviderUtils.ComputeProviderConfig(providersArg, clreventsArg, clreventLevel, profiles);
            EventPipeProvider single = Assert.Single(actual, p => p.Name == "Microsoft-Windows-DotNETRuntime");
            Assert.Equal(expected, single);
        }
    }

    internal static class TestStringExtensions
    {
        extension(string text)
        {
            public string WhereLineContains(string search) => string.Join(Environment.NewLine,
                                                                text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                                                    .Where(l => l.Contains(search, StringComparison.Ordinal)));
        }
    }
}
