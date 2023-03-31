// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

        internal static ulong FindMostCommonPointer(this IEnumerable<ulong> enumerable)
            => (from ptr in enumerable
                group ptr by ptr into g
                orderby g.Count() descending
                select g.First()).First();

        internal static Generation GetGeneration(this ClrObject obj, ClrSegment knownSegment)
        {
            if (knownSegment is null)
            {
                knownSegment = obj.Type.Heap.GetSegmentByAddress(obj);
                if (knownSegment is null)
                {
                    return Generation.Error;
                }
            }

            if (knownSegment.Kind == GCSegmentKind.Ephemeral)
            {
                return knownSegment.GetGeneration(obj) switch
                {
                    0 => Generation.Gen0,
                    1 => Generation.Gen1,
                    2 => Generation.Gen2,
                    _ => Generation.Error
                };
            }

            return knownSegment.Kind switch
            {
                GCSegmentKind.Generation0 => Generation.Gen0,
                GCSegmentKind.Generation1 => Generation.Gen1,
                GCSegmentKind.Generation2 => Generation.Gen2,
                GCSegmentKind.Large => Generation.Large,
                GCSegmentKind.Pinned => Generation.Pinned,
                GCSegmentKind.Frozen => Generation.Frozen,
                _ => Generation.Error
            };
        }
    }
}
