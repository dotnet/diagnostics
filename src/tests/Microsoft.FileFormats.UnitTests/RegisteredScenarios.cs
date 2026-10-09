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
            MemoryBufferAddressSpace source = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            LayoutManager layouts = new LayoutManager().AddPrimitives().AddEnumTypes()
                .RegisterTStruct<Ordered>()
                .RegisterTStruct<Base>()
                .RegisterTStruct<Derived>()
                .RegisterTStruct<Empty>()
                .RegisterTStruct<Nested>()
                .RegisterArray<Ordered>()
                .RegisterTStruct<FixedArray>()
                .RegisterPointer<NodePointer, Node, uint>()
                .RegisterTStruct<Node>()
                .RegisterPointer<Pointer<byte, ulong>, byte, ulong>()
                .AddSizeT(4)
                .RegisterPointer<Pointer<byte, SizeT>, byte, SizeT>();
            Reader reader = new(source, layouts);

            Ordered ordered = reader.Read<Ordered>(0);
            Check(ordered.First == 0x04030201 && ordered.Second == 0x0605, "Metadata field order");
            Check(layouts.GetLayout<Ordered>().Fields.Select(field => field.Name).SequenceEqual(new[] { "First", "Second" }), "Declared field names");
            Check(layouts.GetLayout<Ordered>().Fields.First().Offset == 0, "First field offset");
            Check(layouts.GetLayout<Ordered>().Fields.Last().Offset == 4, "Field alignment");
            Check(!ReferenceEquals(ordered, reader.Read<Ordered>(0)), "Fresh structure instances");

            Derived derived = reader.Read<Derived>(0);
            Check(derived.Secret == 0x04030201 && derived.Tag == 5 && derived.Value == 0x0a09, "Private and inherited fields");
            Check(layouts.GetLayout<Base>().Size == 8 && layouts.GetLayout<Base>().SizeAsBaseType == 8, "Base trailing padding");
            IField secret = layouts.GetLayout<Base>().Fields.First();
            Check((uint)secret.GetValue(derived) == derived.Secret, "Field metadata access");
            secret.SetValue(derived, 17u);
            Check(derived.Secret == 17, "Field metadata mutation");
            Check(reader.Read<Nested>(0).Value.First == ordered.First, "Nested structure");
            Check(reader.Read<Empty>(0) != null, "Empty structure");

            FixedArray fixedArray = reader.Read<FixedArray>(0);
            Check(fixedArray.Values.Length == 2 && fixedArray.Values[1].First == 0x0c0b0a09, "Fixed model array");
            ulong position = 0;
            Ordered[] array = reader.ReadArray<Ordered>(ref position, 2);
            Check(position == 16 && array[1].Second == 0x0e0d, "Typed model array and position");
            Check(reader.ReadArray<byte>(0, 0).Length == 0, "Empty typed array");
            Check(reader.ReadArray<ushort>(0, 2)[1] == 0x0403, "Typed primitive array");
            Check(!ReferenceEquals(fixedArray.Values, reader.Read<FixedArray>(0).Values), "Fresh array instances");

            string[] defines = { "EXTRA" };
            layouts.RegisterTStruct<Optional>(defines);
            defines[0] = "changed";
            Optional optional = reader.Read<Optional>(0);
            Check(optional.Value == 1 && optional.Extra == 0x05040302 && optional.Kind == Kind.Expected, "Pack, enum, and define snapshots");
            LayoutManager withoutDefine = new LayoutManager().AddPrimitives().AddEnumTypes()
                .RegisterTStruct<Optional>();
            Optional omitted = new Reader(source, withoutDefine).Read<Optional>(0);
            Check(omitted.Extra == 0 && (ushort)omitted.Kind == 0x0302, "Excluded conditional field");
            Ordered bigEndian = new Reader(source, new LayoutManager().AddPrimitives(true)
                .RegisterTStruct<Ordered>()).Read<Ordered>(0);
            Check(bigEndian.First == 0x01020304 && bigEndian.Second == 0x0506, "Big endian");

            NodePointer.ConstructorCount = 0;
            layouts.GetLayout<Node>();
            Check(NodePointer.ConstructorCount == 0, "Pointer registration does not construct instances");
            MemoryBufferAddressSpace nodes = new(new byte[] { 1, 0, 0, 0, 8, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 });
            Node node = new Reader(nodes, layouts).Read<Node>(0);
            Check(node.Value == 1 && node.Next.GetType() == typeof(NodePointer), "Concrete pointer type");
            Check(node.Next.Dereference(nodes).Value == 2, "Recursive pointer target");
            Check(NodePointer.ConstructorCount == 2, "Pointer constructors run during reads");
            Check(reader.Read<Pointer<byte, ulong>>(0).Value == 0x0807060504030201, "64-bit pointer");
            Check(reader.Read<Pointer<byte, SizeT>>(0).Value == 0x04030201, "32-bit SizeT pointer");
            LayoutManager wide = new LayoutManager().AddPrimitives().AddSizeT(8)
                .RegisterPointer<Pointer<byte, SizeT>, byte, SizeT>();
            Check(new Reader(source, wide).Read<Pointer<byte, SizeT>>(0).Value == 0x0807060504030201, "64-bit SizeT pointer");
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
