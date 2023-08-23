// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime
{
    internal static class ProcessReaderExtensions
    {
        internal static int GetReadableBytesCount<TRegion>(this CommonMemoryReader _, ImmutableArray<TRegion>.Builder regions, ulong address, int bytesToRead)
            where TRegion : struct, IRegion
        {
            if (bytesToRead <= 0)
            {
                return 0;
            }

            int i = GetRegionContaining(regions, address);
            if (i < 0)
            {
                return 0;
            }

            int bytesReadable;
            ulong prevEndAddr;
            {
                ref readonly TRegion region = ref regions.ItemRef(i);
                ulong regionEndAddr = region.EndAddress;

                long regionSize = (long)(regionEndAddr - address);
                if (regionSize >= bytesToRead)
                {
                    return bytesToRead;
                }

                bytesToRead -= (int)regionSize;
                bytesReadable = (int)regionSize;
                prevEndAddr = regionEndAddr;
            }

            for (i += 1; i < regions.Count; i += 1)
            {
                ref readonly TRegion region = ref regions.ItemRef(i);
                ulong regionBeginAddr = region.BeginAddress;
                ulong regionEndAddr = region.EndAddress;
                if (regionBeginAddr != prevEndAddr || !region.IsReadable)
                {
                    break;
                }

                int regionSize = (int)(regionEndAddr - regionBeginAddr);
                if (regionSize >= bytesToRead)
                {
                    bytesReadable += bytesToRead;
                    break;
                }

                bytesToRead -= regionSize;
                bytesReadable += regionSize;
                prevEndAddr = regionEndAddr;
            }

            return bytesReadable;
        }

        private static int GetRegionContaining<TRegion>(ImmutableArray<TRegion>.Builder regions, ulong address)
            where TRegion : struct, IRegion
        {
            int lower = 0;
            int upper = regions.Count - 1;

            while (lower <= upper)
            {
                int mid = (lower + upper) >> 1;
                ref readonly TRegion region = ref regions.ItemRef(mid);
                ulong beginAddress = region.BeginAddress;
                ulong endAddress = region.EndAddress;

                if (beginAddress <= address && address < endAddress)
                {
                    return region.IsReadable ? mid : -1;
                }

                if (address < beginAddress)
                    upper = mid - 1;
                else
                    lower = mid + 1;
            }

            return -1;
        }
    }
}