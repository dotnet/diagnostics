// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;

namespace Microsoft.FileFormats.Tests
{
    internal static class RegisteredScenarios
    {
        internal static void Run()
        {
            foreach (bool bigEndian in new[] { false, true })
            {
                foreach (int pointerSize in new[] { 4, 8 })
                {
                    LayoutManager layouts = CreateLayouts(bigEndian, pointerSize);
                    ReadCustomModels(layouts, bigEndian);
                    string[] defines = { "EXTRA" };
                    layouts.RegisterTStruct<Optional>(defines);
                    defines[0] = "changed";
                    ReadOptionalFields(layouts, bigEndian, true);
                    LayoutManager withoutDefine = new LayoutManager().AddPrimitives(bigEndian).AddEnumTypes()
                        .RegisterTStruct<Optional>();
                    ReadOptionalFields(withoutDefine, bigEndian, false);
                }
            }
        }

        internal static LayoutManager CreateLayouts(bool bigEndian, int pointerSize)
        {
            return new LayoutManager().AddPrimitives(bigEndian).AddEnumTypes()
                .RegisterTStruct<Ordered>()
                .RegisterTStruct<Base>()
                .RegisterTStruct<Derived>()
                .RegisterTStruct<Empty>()
                .RegisterTStruct<Nested>()
                .RegisterArray<Ordered>()
                .RegisterTStruct<FixedArray>()
                .RegisterArray<ushort>()
                .RegisterTStruct<FixedPrimitiveArray>()
                .RegisterPointer<NodePointer, Node, uint>()
                .RegisterTStruct<Node>()
                .RegisterPointer<Pointer<byte, ulong>, byte, ulong>()
                .AddSizeT(pointerSize)
                .RegisterPointer<Pointer<byte, SizeT>, byte, SizeT>();
        }

        internal static void ReadCustomModels(LayoutManager layouts, bool bigEndian)
        {
            MemoryBufferAddressSpace source = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            Reader reader = new(source, layouts);

            Ordered ordered = reader.Read<Ordered>(0);
            Check(ordered.First == (bigEndian ? 0x01020304u : 0x04030201u) &&
                ordered.Second == (bigEndian ? 0x0506 : 0x0605), "Metadata field order");
            Check(layouts.GetLayout<Ordered>().Fields.Select(field => field.Name).SequenceEqual(new[] { "First", "Second" }), "Declared field names");
            Check(layouts.GetLayout<Ordered>().Fields.First().Offset == 0, "First field offset");
            Check(layouts.GetLayout<Ordered>().Fields.Last().Offset == 4, "Field alignment");
            Check(!ReferenceEquals(ordered, reader.Read<Ordered>(0)), "Fresh structure instances");

            Derived derived = reader.Read<Derived>(0);
            Check(derived.Secret == ordered.First && derived.Tag == 5 &&
                derived.Value == (bigEndian ? 0x090a : 0x0a09), "Private and inherited fields");
            Check(layouts.GetLayout<Base>().Size == 8 && layouts.GetLayout<Base>().SizeAsBaseType == 8, "Base trailing padding");
            IField secret = layouts.GetLayout<Base>().Fields.First();
            Check((uint)secret.GetValue(derived) == derived.Secret, "Field metadata access");
            secret.SetValue(derived, 17u);
            Check(derived.Secret == 17, "Field metadata mutation");
            Check(reader.Read<Nested>(0).Value.First == ordered.First, "Nested structure");
            Check(reader.Read<Empty>(0) != null, "Empty structure");

            FixedArray fixedArray = reader.Read<FixedArray>(0);
            Check(fixedArray.Values.Length == 2 && fixedArray.Values[0].First == ordered.First &&
                fixedArray.Values[1].First == (bigEndian ? 0x090a0b0cu : 0x0c0b0a09u), "Fixed model array");
            Check(!ReferenceEquals(fixedArray.Values[0], fixedArray.Values[1]), "Distinct model array elements");
            FixedPrimitiveArray primitives = reader.Read<FixedPrimitiveArray>(0);
            Check(primitives.Values.SequenceEqual(bigEndian ? new ushort[] { 0x0102, 0x0304, 0x0506 } :
                new ushort[] { 0x0201, 0x0403, 0x0605 }) &&
                primitives.Tail == (bigEndian ? 0x090a0b0cu : 0x0c0b0a09u), "Fixed primitive array and trailing alignment");
            ulong position = 0;
            Ordered[] array = reader.ReadArray<Ordered>(ref position, 2);
            Check(position == 16 && array[1].Second == (bigEndian ? 0x0d0e : 0x0e0d), "Typed model array and position");
            Check(reader.ReadArray<byte>(0, 0).Length == 0, "Empty typed array");
            Check(reader.ReadArray<ushort>(0, 2)[1] == (bigEndian ? 0x0304 : 0x0403), "Typed primitive array");
            Check(!ReferenceEquals(fixedArray.Values, reader.Read<FixedArray>(0).Values), "Fresh array instances");

            NodePointer.ConstructorCount = 0;
            layouts.GetLayout<Node>();
            Check(NodePointer.ConstructorCount == 0, "Pointer registration does not construct instances");
            MemoryBufferAddressSpace nodes = new(bigEndian ?
                new byte[] { 0, 0, 0, 1, 0, 0, 0, 8, 0, 0, 0, 2, 0, 0, 0, 0 } :
                new byte[] { 1, 0, 0, 0, 8, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 });
            Node node = new Reader(nodes, layouts).Read<Node>(0);
            Check(node.Value == 1 && node.Next.GetType() == typeof(NodePointer), "Concrete pointer type");
            Node target = node.Next.Dereference(nodes);
            Check(target.Value == 2 && target.Next.IsNull, "Recursive pointer target and null pointer");
            Check(NodePointer.ConstructorCount == 2, "Pointer constructors run during reads");
            ulong wide = bigEndian ? 0x0102030405060708UL : 0x0807060504030201UL;
            Check(reader.Read<Pointer<byte, ulong>>(0).Value == wide, "64-bit pointer");
            ulong sizeT = layouts.GetLayout<SizeT>().Size == 8 ? wide : ordered.First;
            Check(reader.Read<Pointer<byte, SizeT>>(0).Value == sizeT, "SizeT pointer");
        }

        internal static void ReadOptionalFields(LayoutManager layouts, bool bigEndian, bool enabled)
        {
            MemoryBufferAddressSpace source = new(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
            Optional optional = new Reader(source, layouts).Read<Optional>(0);
            uint extra = enabled ? (bigEndian ? 0x02030405u : 0x05040302u) : 0;
            ushort kind = enabled ? (ushort)(bigEndian ? 0x0607 : 0x0706) : (ushort)(bigEndian ? 0x0203 : 0x0302);
            Check(optional.Value == 1 && optional.Extra == extra && (ushort)optional.Kind == kind, "Pack, enum, and conditional fields");
            ILayout layout = layouts.GetLayout<Optional>();
            Check(layout.Size == (enabled ? 7u : 3u) && layout.NaturalAlignment == 1, "Conditional packed layout");
            Check(layout.Fields.Select(field => field.Offset).SequenceEqual(enabled ? new uint[] { 0, 1, 5 } :
                new uint[] { 0, 1 }), "Conditional field offsets");
        }

        internal static void Check(bool condition, string scenario)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Registered layout scenario failed: " + scenario);
            }
        }

        internal static void ReadCustomHeader(Reader reader, ulong position = 0)
        {
            reader.LayoutManager.RegisterTStruct<Ordered>();
            Ordered header = reader.Read<Ordered>(position);
            Check(header.First == reader.Read<uint>(position) && header.Second == reader.Read<ushort>(position + 4), "Custom model on parser-provided reader");
        }

#pragma warning disable CS0649
        public sealed class Ordered : TStruct
        {
            public uint First;
            public ushort Second;
        }

        public abstract class Base : TStruct
        {
            private uint _secret;
            public byte Tag;
            public uint Secret => _secret;
        }

        public sealed class Derived : Base
        {
            public ushort Value;
        }

        public sealed class Empty : TStruct { }

        public sealed class Nested : TStruct
        {
            public Ordered Value;
        }

        public sealed class FixedArray : TStruct
        {
            [ArraySize(2)]
            public Ordered[] Values;
        }

        public sealed class FixedPrimitiveArray : TStruct
        {
            [ArraySize(3)]
            public ushort[] Values;
            public uint Tail;
        }

        public enum Kind : ushort
        {
            Expected = 0x0706
        }

        [Pack(1)]
        public sealed class Optional : TStruct
        {
            public byte Value;
            [If("EXTRA")]
            public uint Extra;
            public Kind Kind;
        }

        public sealed class Node : TStruct
        {
            public uint Value;
            public NodePointer Next;
        }

        public sealed class NodePointer : Pointer<Node, uint>
        {
            public static int ConstructorCount;

            public NodePointer()
            {
                ConstructorCount++;
            }
        }
#pragma warning restore CS0649
    }
}
