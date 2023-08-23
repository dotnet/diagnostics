// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime
{
    public readonly struct GCDesc
    {
        private static readonly int s_GCDescSize = IntPtr.Size * 2;

        private readonly byte[] _data;

        public bool IsEmpty => _data is null;

        public GCDesc(byte[] data)
        {
            _data = data;
        }

        public IEnumerable<(ulong ReferencedObject, int Offset)> WalkObject(byte[] buffer, int size)
        {
            DebugOnly.Assert(size >= IntPtr.Size);

            int series = GetNumSeries();
            int highest = GetHighestSeries();
            int curr = highest;

            if (series > 0)
            {
                int lowest = GetLowestSeries();
                do
                {
                    long offset = GetSeriesOffset(curr);
                    long stop = offset + GetSeriesSize(curr) + size;

                    while (offset < stop)
                    {
                        ulong ret = new Span<byte>(buffer).AsPointer((int)offset);
                        if (ret != 0)
                            yield return (ret, (int)offset);

                        offset += IntPtr.Size;
                    }

                    curr -= s_GCDescSize;
                } while (curr >= lowest);
            }
            else
            {
                long offset = GetSeriesOffset(curr);

                while (offset < size - IntPtr.Size)
                {
                    for (int i = 0; i > series; i--)
                    {
                        int nptrs = GetPointers(curr, i);
                        int skip = GetSkip(curr, i);

                        long stop = offset + (nptrs * IntPtr.Size);
                        do
                        {
                            ulong ret = new Span<byte>(buffer).AsPointer((int)offset);
                            if (ret != 0)
                                yield return (ret, (int)offset);

                            offset += IntPtr.Size;
                        } while (offset < stop);

                        offset += skip;
                    }
                }
            }
        }

        private int GetPointers(int curr, int i)
        {
            int offset = i * IntPtr.Size;
            if (IntPtr.Size == 4)
                return BitConverter.ToUInt16(_data, curr + offset);

            return BitConverter.ToInt32(_data, curr + offset);
        }

        private int GetSkip(int curr, int i)
        {
            int offset = i * IntPtr.Size + IntPtr.Size / 2;
            if (IntPtr.Size == 4)
                return BitConverter.ToInt16(_data, curr + offset);

            return BitConverter.ToInt32(_data, curr + offset);
        }

        private int GetSeriesSize(int curr)
        {
            if (IntPtr.Size == 4)
                return BitConverter.ToInt32(_data, curr);

            return (int)BitConverter.ToInt64(_data, curr);
        }

        private long GetSeriesOffset(int curr)
        {
            long offset;
            if (IntPtr.Size == 4)
                offset = BitConverter.ToUInt32(_data, curr + IntPtr.Size);
            else
                offset = BitConverter.ToInt64(_data, curr + IntPtr.Size);

            return offset;
        }

        private int GetHighestSeries()
        {
            return _data.Length - IntPtr.Size * 3;
        }

        private int GetLowestSeries()
        {
            return _data.Length - ComputeSize(GetNumSeries());
        }

        private static int ComputeSize(int series)
        {
            return IntPtr.Size + series * IntPtr.Size * 2;
        }

        private int GetNumSeries()
        {
            if (IntPtr.Size == 4)
                return BitConverter.ToInt32(_data, _data.Length - IntPtr.Size);

            return (int)BitConverter.ToInt64(_data, _data.Length - IntPtr.Size);
        }
    }
}
