// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class UncachedMemoryReader : MinidumpMemoryReader
    {
        private readonly ImmutableArray<MinidumpSegment> _segments;
        private readonly Stream _stream;
        private readonly object _sync = new();
        private readonly bool _leaveOpen;

        public UncachedMemoryReader(ImmutableArray<MinidumpSegment> segments, Stream stream, int pointerSize, bool leaveOpen)
        {
            _segments = segments;
            _stream = stream;
            PointerSize = pointerSize;
            _leaveOpen = leaveOpen;
        }

        public override void Dispose()
        {
            if (!_leaveOpen)
                _stream.Dispose();
        }

        public override int PointerSize { get; }

        public override int ReadFromRva(ulong rva, Span<byte> buffer)
        {
            // todo: test bounds
            lock (_sync)
            {
                if ((ulong)_stream.Length <= rva)
                    return 0;

                _stream.Position = (long)rva;
                return _stream.Read(buffer);
            }
        }

        public override int Read(ulong address, Span<byte> buffer)
        {
            if (address == 0 || buffer.Length == 0)
                return 0;

            lock (_sync)
            {
                try
                {
                    int i = GetFirstSegmentContaining(address);
                    if (i < 0)
                        return 0;

                    int bytesRead = 0;
                    for (; i < _segments.Length; i++)
                    {
                        MinidumpSegment segment = _segments[i];
                        ulong virtualAddress = segment.VirtualAddress;
                        ulong size = segment.Size;
                        if (virtualAddress > address)
                            break;

                        if (address >= virtualAddress + size)
                            continue;

                        ulong offset = address - virtualAddress;
                        int toRead = (int)Math.Min(buffer.Length - bytesRead, (long)(size - offset));

                        Span<byte> slice = buffer.Slice(bytesRead, toRead);
                        _stream.Position = (long)(segment.FileOffset + offset);
                        int read = _stream.Read(slice);
                        if (read < toRead)
                            break;

                        bytesRead += read;
                        if (bytesRead == buffer.Length)
                            break;

                        address += (uint)read;
                    }

                    return bytesRead;
                }
                catch (IOException)
                {
                    return 0;
                }
            }
        }

        private int GetFirstSegmentContaining(ulong address)
        {
            int lower = 0;
            int upper = _segments.Length - 1;

            while (lower <= upper)
            {
                int mid = (lower + upper) >> 1;
                MinidumpSegment segment = _segments[mid];

                if (segment.Contains(address))
                {
                    while (mid > 0 && address < _segments[mid - 1].End)
                    {
                        mid--;
                    }

                    return mid;
                }

                if (address < segment.VirtualAddress)
                    upper = mid - 1;
                else
                    lower = mid + 1;
            }

            return -1;
        }
    }
}
