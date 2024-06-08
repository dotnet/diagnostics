// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Microsoft.FileFormats.Tests
{
    public class Layouts
    {
        [Fact]
        public void ReadPrimitives()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Reader reader = new Reader(dataSource);
            Assert.Equal(0x0201, reader.Read<ushort>(0));
            Assert.Equal(0x5, reader.Read<byte>(4));
            Assert.Equal((uint)0x08070605, reader.Read<uint>(4));
        }

#pragma warning disable 0649
        private class SimpleStruct : TStruct
        {
            public int X;
            public short Y;
        }

        [Fact]
        public void ReadTStruct()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Reader reader = new(dataSource);
            SimpleStruct s = reader.Read<SimpleStruct>(1);
            Assert.Equal(0x05040302, s.X);
            Assert.Equal(0x0706, s.Y);
        }

        private class DerivedStruct : SimpleStruct
        {
            public int Z;
        }

        [Fact]
        public void ReadDerivedTStruct()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
            Reader reader = new(dataSource);
            DerivedStruct s = reader.Read<DerivedStruct>(1);
            Assert.Equal(0x05040302, s.X);
            Assert.Equal(0x0706, s.Y);
            Assert.Equal(0x0d0c0b0a, s.Z);
        }

        private class ArrayStruct : TStruct
        {
            [ArraySize(3)]
            public short[] array;
            public int X;
        }

        [Fact]
        public void ReadArrayTStructTest()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
            Reader reader = new(dataSource);
            ArrayStruct s = reader.Read<ArrayStruct>(1);
            Assert.Equal(3, s.array.Length);
            Assert.Equal(0x0302, s.array[0]);
            Assert.Equal(0x0504, s.array[1]);
            Assert.Equal(0x0706, s.array[2]);
            Assert.Equal(0x0d0c0b0a, s.X);
        }

        private enum FooEnum : ushort
        {
            ThreeTwo = 0x0302
        }

        private class EnumStruct : TStruct
        {
            public FooEnum E;
            public int X;
        }

        [Fact]
        public void EnumTStructTest()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
            Reader reader = new(dataSource);
            EnumStruct s = reader.Read<EnumStruct>(1);
            Assert.Equal(FooEnum.ThreeTwo, s.E);
            Assert.Equal(0x09080706, s.X);
        }

        private class VariableSizedPointer<T> : Pointer<T, SizeT> { }
        private class UInt32Pointer<T> : Pointer<T, uint> { }
        private class UInt64Pointer<T> : Pointer<T, ulong> { }
        private class PointerStruct : TStruct
        {
            public VariableSizedPointer<uint> P;
            public UInt32Pointer<byte> P32;
            public UInt64Pointer<ulong> P64;
        }

        [Fact]
        public void PointerTStructTest()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 4, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 });
            LayoutManager mgr = new LayoutManager().AddPrimitives().AddSizeT(4).AddPointerTypes().AddTStructTypes();
            Reader reader = new(dataSource, mgr);
            PointerStruct s = reader.Read<PointerStruct>(0);
            Assert.Equal((ulong)0x4, s.P.Value);
            Assert.False(s.P.IsNull);
            Assert.Equal((uint)0x1, s.P.Dereference(dataSource));
            Assert.Equal((ulong)0x1, s.P32.Value);
            Assert.Equal((byte)0x0, s.P32.Dereference(dataSource));
            Assert.Equal((byte)0x1, s.P32.Element(dataSource, 3));
            Assert.Equal((ulong)0x2, s.P64.Value);
            Assert.Equal((ulong)0x0002000000010000, s.P64.Dereference(dataSource));
        }

        public class OptionalField : TStruct
        {
            public int X;
            [If("A")]
            public int Y;
            public int Z;
        }

        [Fact]
        public void DefineTest()
        {
            MemoryBufferAddressSpace dataSource = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
            LayoutManager a = new LayoutManager().AddPrimitives().AddTStructTypes(new string[] { "A" });
            Reader readerA = new(dataSource, a);
            OptionalField fA = readerA.Read<OptionalField>(0);
            Assert.Equal(0x08070605, fA.Y);
            Assert.Equal(0x0c0b0a09, fA.Z);

            Reader readerB = new(dataSource);
            OptionalField fB = readerB.Read<OptionalField>(0);
            Assert.Equal(0x0, fB.Y);
            Assert.Equal(0x08070605, fB.Z);
        }
    }
}
