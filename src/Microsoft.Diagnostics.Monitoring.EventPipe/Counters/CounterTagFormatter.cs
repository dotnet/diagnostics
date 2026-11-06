// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    // Counter tags arrive from MetricsEventSource as a single string. Newer event versions escape
    // each key and value so a ',' or '=' inside a key/value is distinguishable from the ',' that
    // separates pairs and the '=' that separates a key from its value:
    //   '\' -> '\\'   ',' -> '\,'   '=' -> '\='
    // This mirrors the runtime encoder (System.Diagnostics.Helpers.FormatTags). The escaped form is
    // a transport detail; it must be decoded before tags are shown to a user or written to output.
    internal static class CounterTagFormatter
    {
        // Splits an escaped tag string into its unescaped key/value pairs. Tolerant of malformed
        // input (never throws in release) because it runs on data received over a diagnostic channel.
        // Canonical input escapes '\', ',' and '=' inside a key/value and separates pairs with a bare
        // ',' and a key from its value with the first bare '='. Three shapes that canonical input can
        // never produce are still handled so a mismatched or old encoder cannot crash decoding:
        //   - '\' before any character other than '\', ',' or '=' (e.g. "\a"): the '\' is dropped and
        //     the next character kept. A Debug.Assert flags this as an encoder/decoder mismatch.
        //   - a further bare '=' once inside a value (e.g. the second '=' in "k=val=bad"): appended to
        //     the value verbatim rather than starting another split.
        //   - a lone trailing '\': kept literally.
        public static List<KeyValuePair<string, string>> Decode(string tags)
        {
            if (string.IsNullOrEmpty(tags))
            {
                return [];
            }

            // With no backslash there are no escape sequences, so every '=' and ',' is a separator and
            // each key/value is a verbatim substring of the input. This is the common case (most tags
            // contain no special characters), and slicing it avoids the two StringBuilders and the
            // per-character copy the escaped path needs.
            if (tags.IndexOf('\\') < 0)
            {
                return DecodeUnescaped(tags);
            }

            return DecodeEscaped(tags);
        }

        private static List<KeyValuePair<string, string>> DecodeUnescaped(string tags)
        {
            List<KeyValuePair<string, string>> result = [];

            ReadOnlySpan<char> remaining = tags;
            while (true)
            {
                int comma = remaining.IndexOf(',');
                ReadOnlySpan<char> pair = comma < 0 ? remaining : remaining.Slice(0, comma);

                int separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    result.Add(new KeyValuePair<string, string>(pair.ToString(), string.Empty));
                }
                else
                {
                    result.Add(new KeyValuePair<string, string>(pair.Slice(0, separator).ToString(), pair.Slice(separator + 1).ToString()));
                }

                if (comma < 0)
                {
                    break;
                }

                remaining = remaining.Slice(comma + 1);
            }

            return result;
        }

        private static List<KeyValuePair<string, string>> DecodeEscaped(string tags)
        {
            List<KeyValuePair<string, string>> result = [];

            StringBuilder key = new();
            StringBuilder value = new();
            bool isTokenizingValue = false;

            for (int parseCursor = 0; parseCursor < tags.Length; parseCursor++)
            {
                char c = tags[parseCursor];
                if (c == '\\' && parseCursor + 1 < tags.Length)
                {
                    parseCursor++;
                    char escapedChar = tags[parseCursor];
                    Debug.Assert(escapedChar is '\\' or ',' or '=', $"Unexpected escape sequence '\\{escapedChar}' in tag string '{tags}'.");
                    (isTokenizingValue ? value : key).Append(escapedChar);
                }
                else if (c == '=' && !isTokenizingValue)
                {
                    isTokenizingValue = true;
                }
                else if (c == ',')
                {
                    result.Add(new KeyValuePair<string, string>(key.ToString(), value.ToString()));
                    key.Clear();
                    value.Clear();
                    isTokenizingValue = false;
                }
                else
                {
                    (isTokenizingValue ? value : key).Append(c);
                }
            }

            result.Add(new KeyValuePair<string, string>(key.ToString(), value.ToString()));
            return result;
        }

        // Converts a tag string read from a trace event into the escaped form the rest of the
        // pipeline expects. Payloads from newer events are already escaped. Older payloads are
        // re-escaped here so a later Decode does not swallow a literal '\', ',' or '=' that an old
        // runtime emitted unescaped (for example a value of "C:\temp").
        public static string Normalize(string tags, bool escaped)
        {
            if (string.IsNullOrEmpty(tags))
            {
                return string.Empty;
            }

            return escaped ? tags : Encode(ParseLegacy(tags));
        }

        private static string Encode(List<KeyValuePair<string, string>> pairs)
        {
            StringBuilder builder = new();
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendEscaped(builder, pairs[i].Key);
                builder.Append('=');
                AppendEscaped(builder, pairs[i].Value);
            }

            return builder.ToString();
        }

        // Legacy (unescaped) tag strings split naively: pairs on ',', key/value on the first '='.
        // A value that itself contained ',' or '=' was already indistinguishable in this format, so
        // that ambiguity is inherited here rather than introduced.
        private static List<KeyValuePair<string, string>> ParseLegacy(string tags)
        {
            List<KeyValuePair<string, string>> pairs = new();
            foreach (string pair in tags.Split(','))
            {
                int separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    pairs.Add(new KeyValuePair<string, string>(pair, string.Empty));
                }
                else
                {
                    pairs.Add(new KeyValuePair<string, string>(pair.Substring(0, separator), pair.Substring(separator + 1)));
                }
            }

            return pairs;
        }

        private static void AppendEscaped(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (char c in value)
            {
                if (c is '\\' or ',' or '=')
                {
                    builder.Append('\\');
                }

                builder.Append(c);
            }
        }
    }
}
