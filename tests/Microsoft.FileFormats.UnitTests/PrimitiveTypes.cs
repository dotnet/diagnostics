// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.FileFormats.Tests
{
    public class PrimitiveTypes
    {
        [Fact]
        public void ByteTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 0 });
            Assert.Equal(200, (byte)new UInt8Layout(false).Read(dt, 0));
            Assert.Equal(12, (byte)new UInt8Layout(false).Read(dt, 1));
            Assert.Equal(0, (byte)new UInt8Layout(false).Read(dt, 2));
            Assert.Equal(200, (byte)new UInt8Layout(true).Read(dt, 0));
            Assert.Equal(12, (byte)new UInt8Layout(true).Read(dt, 1));
            Assert.Equal(0, (byte)new UInt8Layout(true).Read(dt, 2));
        }

        [Fact]
        public void SByteTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 0 });
            Assert.Equal(-56, (sbyte)new Int8Layout(false).Read(dt, 0));
            Assert.Equal(12, (sbyte)new Int8Layout(false).Read(dt, 1));
            Assert.Equal(0, (sbyte)new Int8Layout(false).Read(dt, 2));
            Assert.Equal(-56, (sbyte)new Int8Layout(true).Read(dt, 0));
            Assert.Equal(12, (sbyte)new Int8Layout(true).Read(dt, 1));
            Assert.Equal(0, (sbyte)new Int8Layout(true).Read(dt, 2));
        }

        [Fact]
        public void UShortTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 0, 0 });
            Assert.Equal(12 * 256 + 200, (ushort)new UInt16Layout(false).Read(dt, 0));
            Assert.Equal(0, (ushort)new UInt16Layout(false).Read(dt, 2));
            Assert.Equal(200 * 256 + 12, (ushort)new UInt16Layout(true).Read(dt, 0));
            Assert.Equal(0, (ushort)new UInt16Layout(true).Read(dt, 2));
        }

        [Fact]
        public void ShortTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 0, 0 });
            Assert.Equal(12 * 256 + 200, (short)new Int16Layout(false).Read(dt, 0));
            Assert.Equal(0, (short)new Int16Layout(false).Read(dt, 2));
            Assert.Equal(-56 * 256 + 12, (short)new Int16Layout(true).Read(dt, 0));
            Assert.Equal(0, (short)new Int16Layout(true).Read(dt, 2));
        }

        [Fact]
        public void UIntTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 19, 139, 0, 0, 0, 0 });
            Assert.Equal((uint)139 * 256 * 256 * 256 + 19 * 256 * 256 + 12 * 256 + 200, new UInt32Layout(false).Read(dt, 0));
            Assert.Equal((uint)0, new UInt32Layout(false).Read(dt, 4));
            Assert.Equal((uint)200 * 256 * 256 * 256 + 12 * 256 * 256 + 19 * 256 + 139, new UInt32Layout(true).Read(dt, 0));
            Assert.Equal((uint)0, new UInt32Layout(true).Read(dt, 4));
        }

        [Fact]
        public void IntTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 19, 139, 0, 0, 0, 0 });
            Assert.Equal((139 - 256) * 256 * 256 * 256 + 19 * 256 * 256 + 12 * 256 + 200, new Int32Layout(false).Read(dt, 0));
            Assert.Equal(0, new Int32Layout(false).Read(dt, 4));
            Assert.Equal((200 - 256) * 256 * 256 * 256 + 12 * 256 * 256 + 19 * 256 + 139, new Int32Layout(true).Read(dt, 0));
            Assert.Equal(0, new Int32Layout(true).Read(dt, 4));
        }

        [Fact]
        public void ULongTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 19, 139, 192, 7, 1, 40, 0, 0, 0, 0, 0, 0, 0, 0 });
            Assert.Equal((40UL << 56) + (1UL << 48) + (7UL << 40) + (192UL << 32) + (139UL << 24) + (19UL << 16) + (12UL << 8) + 200UL,
                new UInt64Layout(false).Read(dt, 0));
            Assert.Equal(0UL, new UInt64Layout(false).Read(dt, 8));
            Assert.Equal((200UL << 56) + (12UL << 48) + (19UL << 40) + (139UL << 32) + (192UL << 24) + (7UL << 16) + (1UL << 8) + 40UL,
                new UInt64Layout(true).Read(dt, 0));
            Assert.Equal(0UL, new UInt64Layout(true).Read(dt, 8));
        }

        [Fact]
        public void LongTest()
        {
            MemoryBufferAddressSpace dt = new MemoryBufferAddressSpace(new byte[] { 200, 12, 19, 139, 192, 7, 1, 40, 0, 0, 0, 0, 0, 0, 0, 0 });
            Assert.Equal((40L << 56) + (1L << 48) + (7L << 40) + (192L << 32) + (139L << 24) + (19L << 16) + (12L << 8) + 200L,
                new Int64Layout(false).Read(dt, 0));
            Assert.Equal(0L, new Int64Layout(false).Read(dt, 8));
            Assert.Equal((-56L << 56) + (12L << 48) + (19L << 40) + (139L << 32) + (192L << 24) + (7L << 16) + (1L << 8) + 40L,
                new Int64Layout(true).Read(dt, 0));
            Assert.Equal(0L, new Int64Layout(true).Read(dt, 8));
        }
    }
}
