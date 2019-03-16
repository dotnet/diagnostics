using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public class LoggerSpec
    {
        // Handles case normalization because key lookup is case-insensitive.
        private static readonly Dictionary<string, string> _levelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Trace", "Trace" },
            { "Debug", "Debug" },
            { "Information", "Information" },
            { "Warning", "Warning" },
            { "Error", "Error" },
            { "Critical", "Critical" },
        };

        public string Prefix { get; }
        public string Level { get; }

        public LoggerSpec(string prefix, string level)
        {
            Prefix = prefix;
            Level = level;
        }

        public static bool TryParse(string input, out LoggerSpec spec)
        {
            var splat = input.Split(':');

            var prefix = splat[0];
            string level = null;
            if (splat.Length > 1)
            {
                if (!_levelMap.TryGetValue(splat[1], out level))
                {
                    spec = null;
                    return false;
                }
            }

            spec = new LoggerSpec(prefix, level);
            return true;
        }
    }
}
