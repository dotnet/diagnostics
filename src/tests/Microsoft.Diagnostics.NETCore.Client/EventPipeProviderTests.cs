// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Xunit;

namespace Microsoft.Diagnostics.NETCore.Client
{

    /// <summary>
    /// Suite of tests that test top-level commands
    /// </summary>
    public class EventPipeProviderTests
    {
        [Fact]
        public void EqualTest1()
        {
            EventPipeProvider provider1 = new EventPipeProvider("myProvider", EventLevel.Informational);
            EventPipeProvider provider2 = new EventPipeProvider("myProvider", EventLevel.Informational);
            Assert.True(provider1 == provider2);
        }

        [Fact]
        public void EqualTest2()
        {
            EventPipeProvider provider1 = new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, (long)(-1));
            EventPipeProvider provider2 = new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, (long)(-1));
            Assert.True(provider1 == provider2);
        }

        [Fact]
        public void EqualTest3()
        {
            EventPipeProvider provider1 = new EventPipeProvider(
                "System.Runtime",
                EventLevel.Verbose,
                (long)(-1),
                new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "1" }
                });
            EventPipeProvider provider2 = new EventPipeProvider(
                "System.Runtime",
                EventLevel.Verbose,
                (long)(-1),
                new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "1" }
                });
            Assert.True(provider1 == provider2);
        }

        [Fact]
        public void InEqualityTest()
        {
            var providers = new EventPipeProvider[5];
            providers[0] = new EventPipeProvider("myProvider", EventLevel.Informational);
            providers[1] = new EventPipeProvider("myProvider", EventLevel.Informational, (long)(-1));
            providers[2] = new EventPipeProvider("myProvider", EventLevel.Verbose, (long)(-1));
            providers[3] = new EventPipeProvider(
                "myProvider",
                EventLevel.Verbose,
                (long)(-1),
                new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "1" }
                });
            providers[4] = new EventPipeProvider(
                "myProvider",
                EventLevel.Verbose,
                (long)(-1),
                new Dictionary<string, string>() {
                    { "EventCounterIntervalSec", "2" }
                });

            for (int i = 0; i < providers.Length - 1; i++)
            {
                for (int j = i + 1; j < providers.Length; j++)
                {
                    Assert.True(providers[i] != providers[j]);
                }
            }
        }

        [Fact]
        public void ToStringTest1()
        {
            var provider = new EventPipeProvider("MyProvider", EventLevel.Verbose, (long)(0xdeadbeef));
            Assert.Equal("MyProvider:0x00000000DEADBEEF:5", provider.ToString());
        }

        [Fact]
        public void ToStringTest2()
        {
            var provider1 = new EventPipeProvider("MyProvider", EventLevel.Verbose, (long)(0xdeadbeef),
                new Dictionary<string, string>()
                {
                    { "key1", "value1" },
                });
            var provider2 = new EventPipeProvider("MyProvider", EventLevel.Verbose, (long)(0xdeadbeef),
                new Dictionary<string, string>()
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                });
            Assert.Equal("MyProvider:0x00000000DEADBEEF:5:key1=value1", provider1.ToString());
            Assert.Equal("MyProvider:0x00000000DEADBEEF:5:key1=value1;key2=value2", provider2.ToString());
        }

        [Fact]
        public void DiagnosticSourceArgumentStringTestWithEscapedValue1()
        {
            string diagnosticFilterString = "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
                "Request.Path" +
                ";Request.Method" +
                "\r\n";

            var provider = new EventPipeProvider("DiagnosticSourceProvider", EventLevel.Verbose, (long)(0xdeadbeef),
                new Dictionary<string, string>()
                {
                    { "FilterAndPayloadSpecs", diagnosticFilterString }
                });

            Assert.Equal("DiagnosticSourceProvider:0x00000000DEADBEEF:5:FilterAndPayloadSpecs=\"Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-Request.Path;Request.Method\r\n\"",
                provider.ToString());
        }


        [Fact]
        public void DiagnosticSourceArgumentStringTestWithEscapedValue2()
        {
            string diagnosticFilterString = "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
                "Request.Path" +
                ";Request.Method" +
                ";RequestName=SomeRequest" +
                "\r\n";

            var provider = new EventPipeProvider("DiagnosticSourceProvider", EventLevel.Verbose, (long)(0xdeadbeef),
                new Dictionary<string, string>()
                {
                    { "FilterAndPayloadSpecs", diagnosticFilterString }
                });

            Assert.Equal("DiagnosticSourceProvider:0x00000000DEADBEEF:5:FilterAndPayloadSpecs=\"Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-Request.Path;Request.Method;RequestName=SomeRequest\r\n\"",
                provider.ToString());
        }

        [Fact]
        public void DiagnosticSourceArgumentStringTestWithEscapedKey()
        {
            string diagnosticFilterString = "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
                "Request.Path" +
                ";Request.Method" +
                ";RequestName=SomeRequest" +
                "\r\n";

            var provider = new EventPipeProvider("DiagnosticSourceProvider", EventLevel.Verbose, (long)(0xdeadbeef),
                new Dictionary<string, string>()
                {
                    { "ArgumentKeyWith;Semicolon=Equal", diagnosticFilterString }
                });

            Assert.Equal("DiagnosticSourceProvider:0x00000000DEADBEEF:5:\"ArgumentKeyWith;Semicolon=Equal\"=\"Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-Request.Path;Request.Method;RequestName=SomeRequest\r\n\"",
                provider.ToString());
        }

        [Fact]
        public void DiagnosticSourceArgumentStringTestWithManyArgs()
        {
            string diagnosticFilterString = "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
                "Request.Path" +
                ";Request.Method" +
                ";RequestName=SomeRequest" +
                "\r\n";

            var provider = new EventPipeProvider("DiagnosticSourceProvider", EventLevel.Verbose, (long)(0xdeadbeef),
                new Dictionary<string, string>()
                {
                    { "ArgumentKeyWith;Semicolon=Equal", diagnosticFilterString },
                    { "ArgumentKeyWith;Semicolon=Equal2", diagnosticFilterString }
                });

            Assert.Equal("DiagnosticSourceProvider:0x00000000DEADBEEF:5:\"ArgumentKeyWith;Semicolon=Equal\"=\"Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-Request.Path;Request.Method;RequestName=SomeRequest\r\n\";\"ArgumentKeyWith;Semicolon=Equal2\"=\"Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-Request.Path;Request.Method;RequestName=SomeRequest\r\n\"",
                provider.ToString());
        }
    }
}
