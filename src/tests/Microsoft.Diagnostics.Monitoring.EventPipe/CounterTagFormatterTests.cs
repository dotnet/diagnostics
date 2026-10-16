// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class CounterTagFormatterTests
    {
        // These canonical strings must match the runtime encoder
        // (System.Diagnostics.Helpers.FormatTags / AppendEscaped) byte-for-byte.
        [Theory]
        [InlineData("plain=simple", "plain", "simple")]
        [InlineData(@"comma=a\,b\,c", "comma", "a,b,c")]
        [InlineData(@"equals=x\=1", "equals", "x=1")]
        [InlineData(@"url=/api/items?filter\=red\,blue&sort\=name", "url", "/api/items?filter=red,blue&sort=name")]
        [InlineData(@"path=C:\\temp", "path", @"C:\temp")]
        public void Decode_SinglePair_UnescapesKeyAndValue(string encoded, string expectedKey, string expectedValue)
        {
            List<KeyValuePair<string, string>> pairs = CounterTagFormatter.Decode(encoded);

            KeyValuePair<string, string> pair = Assert.Single(pairs);
            Assert.Equal(expectedKey, pair.Key);
            Assert.Equal(expectedValue, pair.Value);
        }

        [Fact]
        public void Decode_MultiplePairs_SplitsOnUnescapedComma()
        {
            List<KeyValuePair<string, string>> pairs = CounterTagFormatter.Decode(@"comma=a\,b\,c,equals=x\=1");

            Assert.Equal(2, pairs.Count);
            Assert.Equal(new KeyValuePair<string, string>("comma", "a,b,c"), pairs[0]);
            Assert.Equal(new KeyValuePair<string, string>("equals", "x=1"), pairs[1]);
        }

        [Fact]
        public void Decode_EmptyString_ReturnsZeroPairs()
        {
            Assert.Empty(CounterTagFormatter.Decode(string.Empty));
            Assert.Empty(CounterTagFormatter.Decode(null));
        }

        [Theory]
        [InlineData("key=", "key", "")]
        [InlineData("=value", "", "value")]
        [InlineData("=", "", "")]
        public void Decode_EmptyKeyOrValue_Roundtrips(string encoded, string expectedKey, string expectedValue)
        {
            KeyValuePair<string, string> pair = Assert.Single(CounterTagFormatter.Decode(encoded));
            Assert.Equal(expectedKey, pair.Key);
            Assert.Equal(expectedValue, pair.Value);
        }

        [Fact]
        public void Decode_MissingSeparator_YieldsEmptyValue()
        {
            // A pair with no '=' must not throw (this was the original ConsoleWriter crash).
            KeyValuePair<string, string> pair = Assert.Single(CounterTagFormatter.Decode("keyonly"));
            Assert.Equal("keyonly", pair.Key);
            Assert.Equal(string.Empty, pair.Value);
        }

        [Fact]
        public void Decode_TrailingBackslash_TreatedAsLiteral()
        {
            // A lone trailing backslash never occurs in canonical output, but decode must be defensive.
            KeyValuePair<string, string> pair = Assert.Single(CounterTagFormatter.Decode(@"key=value\"));
            Assert.Equal("key", pair.Key);
            Assert.Equal(@"value\", pair.Value);
        }

        [Fact]
        public void Decode_ExtraUnescapedEqualsInValue_KeptLiterally()
        {
            // A second bare '=' inside a value never occurs in canonical output (it would be escaped
            // as "\="), but decode keeps it verbatim in the value rather than throwing.
            KeyValuePair<string, string> pair = Assert.Single(CounterTagFormatter.Decode("k=val=bad"));
            Assert.Equal("k", pair.Key);
            Assert.Equal("val=bad", pair.Value);
        }

        [Fact]
        public void Normalize_Escaped_ReturnsInputUnchanged()
        {
            const string escaped = @"comma=a\,b\,c,equals=x\=1";
            Assert.Equal(escaped, CounterTagFormatter.Normalize(escaped, escaped: true));
        }

        [Fact]
        public void Normalize_Legacy_ReEscapesSpecialCharacters()
        {
            // A '\' or '=' inside a legacy value must be escaped so a later Decode recovers it.
            Assert.Equal(@"path=C:\\temp", CounterTagFormatter.Normalize(@"path=C:\temp", escaped: false));
            Assert.Equal(@"expr=a\=b", CounterTagFormatter.Normalize("expr=a=b", escaped: false));

            // Simple values are unchanged.
            Assert.Equal("a=1,b=2", CounterTagFormatter.Normalize("a=1,b=2", escaped: false));
        }

        [Fact]
        public void Normalize_LegacyRoundTripsThroughDecode()
        {
            string canonical = CounterTagFormatter.Normalize(@"path=C:\temp", escaped: false);
            KeyValuePair<string, string> pair = Assert.Single(CounterTagFormatter.Decode(canonical));
            Assert.Equal("path", pair.Key);
            Assert.Equal(@"C:\temp", pair.Value);
        }

        [Fact]
        public void Normalize_EmptyOrNull_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, CounterTagFormatter.Normalize(string.Empty, escaped: true));
            Assert.Equal(string.Empty, CounterTagFormatter.Normalize(null, escaped: false));
        }
    }
}
