// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal static class CounterUtilities
    {
        //The metadata payload is formatted as a string of comma separated key:value pairs.
        //This limitation means that metadata values cannot include commas; otherwise, the
        //metadata will be parsed incorrectly. If a value contains a comma, then all metadata
        //is treated as invalid and excluded from the payload.
        public static IDictionary<string, string> GetMetadata(string metadataPayload, char kvSeparator = ':')
        {
            var metadataDict = new Dictionary<string, string>();

            ReadOnlySpan<char> metadata = metadataPayload;

            while (!metadata.IsEmpty)
            {
                int commaIndex = metadata.IndexOf(',');

                ReadOnlySpan<char> kvPair;

                if (commaIndex < 0)
                {
                    kvPair = metadata;
                    metadata = default;
                }
                else
                {
                    kvPair = metadata[..commaIndex];
                    metadata = metadata.Slice(commaIndex + 1);
                }

                int colonIndex = kvPair.IndexOf(kvSeparator);
                if (colonIndex < 0)
                {
                    metadataDict.Clear();
                    break;
                }

                string metadataKey = kvPair[..colonIndex].ToString();
                string metadataValue = kvPair.Slice(colonIndex + 1).ToString();
                metadataDict[metadataKey] = metadataValue;
            }

            return metadataDict;
        }

        public static string AppendPercentile(string tags, double quantile) => AppendPercentile(tags, FormattableString.Invariant($"Percentile={(int)(100 * quantile)}"));

        private static string AppendPercentile(string tags, string percentile) => string.IsNullOrEmpty(tags) ? percentile : string.Concat(tags, ",", percentile);
    }
}
