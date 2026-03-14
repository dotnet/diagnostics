// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal static class ExtensionMethodHelpers
    {
        public static string ConvertToHumanReadable(this ulong totalBytes) => ConvertToHumanReadable((double)totalBytes);

        public static string ConvertToHumanReadable(this long totalBytes) => ConvertToHumanReadable((double)totalBytes);

        public static string ConvertToHumanReadable(this double totalBytes)
        {
            double updated = totalBytes;

            updated /= 1024;
            if (updated < 1024)
            {
                return $"{updated:0.00}kb";
            }

            updated /= 1024;
            if (updated < 1024)
            {
                return $"{updated:0.00}mb";
            }

            updated /= 1024;
            return $"{updated:0.00}gb";
        }

        public static string ToSignedHexString(this int offset) => offset < 0 ? $"-{Math.Abs(offset):x2}" : offset.ToString("x2");

        internal static ulong FindMostCommonPointer(this IEnumerable<ulong> enumerable)
            => (from ptr in enumerable
                group ptr by ptr into g
                orderby g.Count() descending
                select g.First()).First();
    }
}
