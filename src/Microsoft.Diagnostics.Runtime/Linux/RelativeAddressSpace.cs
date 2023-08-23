// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class RelativeAddressSpace : IAddressSpace
    {
        private readonly IAddressSpace _baseAddressSpace;
        private readonly ulong _baseStart;
        private readonly ulong _length;
        private readonly long _baseToRelativeShift;
        private readonly string _name;

        public string Name => _name is null ? _baseAddressSpace.Name : $"{_baseAddressSpace.Name}:{_name}";

        public RelativeAddressSpace(IAddressSpace baseAddressSpace, string name, ulong startOffset, ulong length)
            : this(baseAddressSpace, name, startOffset, length, -(long)startOffset)
        {
        }

        public RelativeAddressSpace(IAddressSpace baseAddressSpace, string name, ulong startOffset, ulong length, long baseToRelativeShift)
        {
            _baseAddressSpace = baseAddressSpace;
            _baseStart = startOffset;
            _length = length;
            _baseToRelativeShift = baseToRelativeShift;
            _name = name;
        }

        public int Read(ulong position, Span<byte> buffer)
        {
            if ((long)position < _baseToRelativeShift)
                return 0;

            ulong basePosition = (ulong)((long)position - _baseToRelativeShift);
            if (basePosition < _baseStart)
                return 0;

            if (_length < (ulong)buffer.Length)
                buffer = buffer.Slice(0, (int)_length);

            return _baseAddressSpace.Read(basePosition, buffer);
        }

        public ulong Length => (ulong)((long)(_baseStart + _length) + _baseToRelativeShift);
    }
}
