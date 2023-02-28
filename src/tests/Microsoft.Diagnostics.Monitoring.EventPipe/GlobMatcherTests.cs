// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    public class GlobMatcherTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public GlobMatcherTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        private const string Path1 = "/";
        private const string Path2 = "/Home/Privacy";
        private const string Path3 = "/Home/test.js";
        private const string Path4 = "/Home/Sub/Path";
        private const string Path5 = "/Home/Sub/Path/test.js";
        private const string Path6 = "/Privacy/Sub/Path/test.html";
        private const string Path7 = "/About/style.css";

        private readonly static IReadOnlyList<string> Paths = new[] { Path1, Path2, Path3, Path4, Path5, Path6, Path7 };

        private readonly static Dictionary<string, IReadOnlyList<string>> Patterns = new()
        {
            { "**/*", Paths },
            { "**/*.j", Array.Empty<string>() },
            { "**/Sub/**/*", new[] { Path4, Path5, Path6 } },
            { "/", new[] { Path1 } },
            { "**/*.js", new[] { Path3, Path5 } },
            { "/Home/**/*", new[] { Path2, Path3, Path4, Path5 } }
        };

        [Fact]
        public void TestGlobs()
        {
            foreach(KeyValuePair<string, IReadOnlyList<string>> keyValuePair in Patterns)
            {
                var matcher = new GlobMatcher(new[] { keyValuePair.Key }, null);

                foreach(string value in keyValuePair.Value)
                {
                    if (!matcher.Match(value))
                    {
                        Assert.True(false, $"Expected {value} to match pattern {keyValuePair.Key}");
                    };
                }

                foreach(string value in Paths.Except(keyValuePair.Value))
                {
                    if (matcher.Match(value))
                    {
                        Assert.False(true, $"Expected {value} to not match pattern {keyValuePair.Key}");
                    }
                }
            }
        }

        [Fact]
        public void TestMultiplePatterns()
        {
            var matcher = new GlobMatcher(new[] { "**/*" }, new[] { "**/*.js", "**/*.css" });

            Assert.True(matcher.Match(Path1));
            Assert.True(matcher.Match(Path2));
            Assert.False(matcher.Match(Path3));
            Assert.True(matcher.Match(Path4));
            Assert.False(matcher.Match(Path5));
            Assert.True(matcher.Match(Path6));
            Assert.False(matcher.Match(Path7));
        }
    }
}