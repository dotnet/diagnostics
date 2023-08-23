// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed class MemoryVirtualAddressSpace : IAddressSpace
    {
        private readonly IDataReader _dataReader;

        public MemoryVirtualAddressSpace(IDataReader dataReader)
        {
            _dataReader = dataReader;
        }

        public ulong Length => throw new NotImplementedException();

        public string Name => nameof(MemoryVirtualAddressSpace);

        public int Read(ulong position, Span<byte> buffer)
        {
            return _dataReader.Read(position, buffer);
        }
    }
}