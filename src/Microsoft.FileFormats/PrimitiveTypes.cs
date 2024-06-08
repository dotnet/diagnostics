// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    public abstract class PrimitiveTypeLayout : LayoutBase
    {
        public PrimitiveTypeLayout(Type type, bool isBigEndian, uint size) : base(type, size)
        {
            IsBigEndian = isBigEndian;
        }
        public bool IsBigEndian { get; private set; }
    }

    /// <summary>
    /// TypeParser for System.Int8
    /// </summary>
    public class BoolLayout : PrimitiveTypeLayout
    {
        public BoolLayout(Type type, bool isBigEndian) : base(type, isBigEndian, 1) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 1);
            return buffer[0] != 0;
        }
    }

    /// <summary>
    /// TypeParser for System.Int8
    /// </summary>
    public class Int8Layout : PrimitiveTypeLayout
    {
        public Int8Layout(bool isBigEndian) : this(typeof(sbyte), isBigEndian) { }
        public Int8Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 1) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 1);
            return unchecked((sbyte)buffer[0]);
        }
    }

    /// <summary>
    /// TypeParser for System.UInt8
    /// </summary>
    public class UInt8Layout : PrimitiveTypeLayout
    {
        public UInt8Layout(bool isBigEndian) : this(typeof(byte), isBigEndian) { }
        public UInt8Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 1) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 1);
            return buffer[0];
        }
    }

    /// <summary>
    /// TypeParser for System.Char.
    /// </summary>
    public class CharLayout : PrimitiveTypeLayout
    {
        public CharLayout(bool isBigEndian) : this(typeof(char), isBigEndian) { }
        public CharLayout(Type type, bool isBigEndian) : base(type, isBigEndian, 2) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 2);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToChar(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.Int16
    /// </summary>
    public class Int16Layout : PrimitiveTypeLayout
    {
        public Int16Layout(bool isBigEndian) : this(typeof(short), isBigEndian) { }
        public Int16Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 2) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 2);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt16(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.UInt16
    /// </summary>
    public class UInt16Layout : PrimitiveTypeLayout
    {
        public UInt16Layout(bool isBigEndian) : this(typeof(ushort), isBigEndian) { }
        public UInt16Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 2) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 2);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt16(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.Int32
    /// </summary>
    public class Int32Layout : PrimitiveTypeLayout
    {
        public Int32Layout(bool isBigEndian) : this(typeof(int), isBigEndian) { }
        public Int32Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 4) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 4);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt32(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.UInt32
    /// </summary>
    public class UInt32Layout : PrimitiveTypeLayout
    {
        public UInt32Layout(bool isBigEndian) : this(typeof(uint), isBigEndian) { }
        public UInt32Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 4) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 4);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt32(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.Single
    /// </summary>
    public class SingleLayout : PrimitiveTypeLayout
    {
        public SingleLayout(bool isBigEndian) : this(typeof(float), isBigEndian) { }
        public SingleLayout(Type type, bool isBigEndian) : base(type, isBigEndian, 4) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 4);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                byte temp = buffer[0];
                buffer[0] = buffer[3];
                buffer[3] = temp;
                temp = buffer[1];
                buffer[1] = buffer[2];
                buffer[2] = temp;
            }
            return BitConverter.ToSingle(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.Int64
    /// </summary>
    public class Int64Layout : PrimitiveTypeLayout
    {
        public Int64Layout(bool isBigEndian) : this(typeof(long), isBigEndian) { }
        public Int64Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 8) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 8);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToInt64(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.UInt64
    /// </summary>
    public class UInt64Layout : PrimitiveTypeLayout
    {
        public UInt64Layout(bool isBigEndian) : this(typeof(ulong), isBigEndian) { }
        public UInt64Layout(Type type, bool isBigEndian) : base(type, isBigEndian, 8) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 8);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                Array.Reverse(buffer);
            }
            return BitConverter.ToUInt64(buffer, 0);
        }
    }

    /// <summary>
    /// TypeParser for System.Double
    /// </summary>
    public class DoubleLayout : PrimitiveTypeLayout
    {
        public DoubleLayout(bool isBigEndian) : this(typeof(double), isBigEndian) { }
        public DoubleLayout(Type type, bool isBigEndian) : base(type, isBigEndian, 8) { }
        public override object Read(IAddressSpace dataSource, ulong position)
        {
            byte[] buffer = dataSource.Read(position, 4);
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                byte temp = buffer[0];
                buffer[0] = buffer[7];
                buffer[7] = temp;
                temp = buffer[1];
                buffer[1] = buffer[6];
                buffer[6] = temp;
                temp = buffer[2];
                buffer[2] = buffer[5];
                buffer[5] = temp;
                temp = buffer[3];
                buffer[3] = buffer[4];
                buffer[4] = temp;
            }
            return BitConverter.ToDouble(buffer, 0);
        }
    }

    public static partial class LayoutManagerExtensions
    {
        /// <summary>
        /// Adds supports for reading bool, sbyte, byte, char, short, ushort, int, uint, long, ulong, float, and double
        /// </summary>
        /// <param name="layouts">The layout manager that will hold the new layout</param>
        /// <param name="isBigEndian">True if the primitives should be read in big endian byte order, otherwise little endian</param>
        public static LayoutManager AddPrimitives(this LayoutManager layouts, bool isBigEndian = false)
        {
            layouts.AddLayout(new BoolLayout(typeof(bool), isBigEndian));
            layouts.AddLayout(new Int8Layout(typeof(sbyte), isBigEndian));
            layouts.AddLayout(new UInt8Layout(typeof(byte), isBigEndian));
            layouts.AddLayout(new CharLayout(typeof(char), isBigEndian));
            layouts.AddLayout(new Int16Layout(typeof(short), isBigEndian));
            layouts.AddLayout(new UInt16Layout(typeof(ushort), isBigEndian));
            layouts.AddLayout(new Int32Layout(typeof(int), isBigEndian));
            layouts.AddLayout(new UInt32Layout(typeof(uint), isBigEndian));
            layouts.AddLayout(new Int64Layout(typeof(long), isBigEndian));
            layouts.AddLayout(new UInt64Layout(typeof(ulong), isBigEndian));
            layouts.AddLayout(new SingleLayout(typeof(float), isBigEndian));
            layouts.AddLayout(new DoubleLayout(typeof(double), isBigEndian));
            return layouts;
        }
    }
}
