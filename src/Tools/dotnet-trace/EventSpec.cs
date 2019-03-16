using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;
using Microsoft.Internal.Utilities;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public class EventSpec
    {
        public string Provider { get; }
        public ulong Keywords { get; }
        public EventLevel Level { get; }
        public IDictionary<string, string> Parameters { get; }

        public EventSpec(string provider, ulong keywords, EventLevel level)
            : this(provider, keywords, level, new Dictionary<string, string>())
        {
        }

        public EventSpec(string provider, ulong keywords, EventLevel level, IDictionary<string, string> parameters)
        {
            Provider = provider;
            Keywords = keywords;
            Level = level;
            Parameters = parameters;
        }

        public static bool TryParse(string input, out EventSpec spec)
        {
            spec = null;
            var splat = input.Split(':');

            if (splat.Length == 0)
            {
                return false;
            }

            var provider = splat[0];
            var keywords = ulong.MaxValue;
            var level = EventLevel.Verbose;
            var parameters = new Dictionary<string, string>();

            if (splat.Length > 1)
            {
                if (!TryParseKeywords(splat[1], provider, out keywords))
                {
                    return false;
                }
            }

            if (splat.Length > 2)
            {
                if (!TryParseLevel(splat[2], out level))
                {
                    return false;
                }
            }

            if (splat.Length > 3)
            {
                if (!TryParseParameters(splat[3], parameters))
                {
                    return false;
                }
            }

            spec = new EventSpec(provider, keywords, level, parameters);
            return true;
        }

        public string ToConfigString()
        {
            var config = $"{Provider}:0x{Keywords:X}:{(int)Level}";
            if(Parameters.Count > 0)
            {
                config += $":{FormatParameters(Parameters)}";
            }
            return config;
        }

        private static string FormatParameters(IDictionary<string, string> parameters)
        {
            var builder = new StringBuilder();
            foreach(var (key, value) in parameters)
            {
                builder.Append($"{key}={value};");
            }

            // Remove the trailing ';'
            builder.Length -= 1;

            return builder.ToString();
        }

        private static bool TryParseParameters(string input, IDictionary<string, string> parameters)
        {
            var splat = input.Split(';');
            foreach(var item in splat)
            {
                var splot = item.Split('=');
                if(splot.Length != 2)
                {
                    return false;
                }

                parameters[splot[0]] = splot[1];
            }

            return true;
        }

        private static bool TryParseLevel(string input, out EventLevel level)
        {
            level = EventLevel.Verbose;
            if (int.TryParse(input, out var intLevel))
            {
                if (intLevel >= (int)EventLevel.LogAlways && intLevel <= (int)EventLevel.Verbose)
                {
                    level = (EventLevel)intLevel;
                    return true;
                }
            }
            else if (Enum.TryParse(input, ignoreCase: true, out level))
            {
                return true;
            }
            return false;
        }

        private static bool TryParseKeywords(string input, string provider, out ulong keywords)
        {
            if (string.Equals("*", input, StringComparison.Ordinal))
            {
                keywords = ulong.MaxValue;
                return true;
            }
            else if (input.StartsWith("0x"))
            {
                // Keywords
                if (ulong.TryParse(input, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out keywords))
                {
                    return true;
                }
            }
            else if(KnownData.TryGetProvider(provider, out var knownProvider))
            {
                var splat = input.Split(',');
                keywords = 0;
                foreach(var item in splat)
                {
                    if(knownProvider.Keywords.TryGetValue(item, out var knownKeyword))
                    {
                        keywords |= knownKeyword.Value;
                    }
                    else
                    {
                        throw new CommandLineException($"Keyword '{item}' is not a well-known keyword for '{provider}'");
                    }
                }
                return true;
            }

            keywords = ulong.MaxValue;
            return false;
        }
    }
}
