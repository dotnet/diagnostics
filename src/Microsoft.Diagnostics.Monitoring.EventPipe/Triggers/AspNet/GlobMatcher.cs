// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers.AspNet
{
    /// <summary>
    /// Uses regular expressions to match globs.
    /// </summary>
    /// <remarks>
    /// Note there are some differences in glob patterns. Specifically **/ is supported, but **
    /// by itself is not.
    /// </remarks>
    internal sealed class GlobMatcher
    {
        private readonly Regex _includeRegex;
        private readonly Regex _excludeRegex;

        //We convert all **/ to a regex that matches 0 or more path segments
        private const string EscapedGlobstarDirectory = @"\*\*/";
        private const string GlobstarDirectoryRegex = @"([^/]*/)*";

        //Convert all * matches to any character other than a path separator
        private const string EscapedWildcard = @"\*";
        private const string WildcardRegex = @"[^/]*";
        private const string StartRegex = "^";
        private const string EndRegex = "$";

        public GlobMatcher(string[] includes, string[] excludes)
        {
            if (includes?.Any() == true)
            {
                _includeRegex = new Regex(string.Join("|", includes.Select(TransformPattern)),
                   RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (excludes?.Any() == true)
            {
                _excludeRegex = new Regex(string.Join("|", excludes.Select(TransformPattern)),
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        private static string TransformPattern(string globPattern) =>
            string.Concat(StartRegex,
                Regex.Escape(globPattern)
                //Note the order is important to make sure globstar gets transformed first.
                .Replace(EscapedGlobstarDirectory, GlobstarDirectoryRegex)
                .Replace(EscapedWildcard, WildcardRegex),
                EndRegex);

        public bool Match(string input)
        {
            if (_excludeRegex?.IsMatch(input) == true)
            {
                return false;
            }

            return _includeRegex?.IsMatch(input) ?? true ? true : false;
        }
    }
}
