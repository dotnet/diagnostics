// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A representation of a field in the target process.
    /// </summary>
    public abstract class ClrField : IClrField
    {
        /// <summary>
        /// Gets the <see cref="ClrType"/> containing this field.
        /// </summary>
        public abstract ClrType ContainingType { get; }

        IClrType IClrField.ContainingType => ContainingType;

        /// <summary>
        /// Gets the name of the field.
        /// </summary>
        public abstract string? Name { get; }

        /// <summary>
        /// Gets the type token of this field.
        /// </summary>
        public abstract int Token { get; }

        /// <summary>
        /// Gets the type of the field.  Note this property may return <see langword="null"/> on error.  There is a bug in several versions
        /// of our debugging layer which causes this.  You should always null-check the return value of this field.
        /// </summary>
        public abstract ClrType? Type { get; }

        IClrType? IClrField.Type => Type;

        /// <summary>
        /// Gets the element type of this field.  Note that even when Type is <see langword="null"/>, this should still tell you
        /// the element type of the field.
        /// </summary>
        public abstract ClrElementType ElementType { get; }

        /// <summary>
        /// Gets a value indicating whether this field is a primitive (<see cref="int"/>, <see cref="float"/>, etc).
        /// </summary>
        /// <returns>True if this field is a primitive (<see cref="int"/>, <see cref="float"/>, etc), false otherwise.</returns>
        public virtual bool IsPrimitive => ElementType.IsPrimitive();

        /// <summary>
        /// Gets a value indicating whether this field is a value type.
        /// </summary>
        /// <returns>True if this field is a value type, false otherwise.</returns>
        public virtual bool IsValueType => ElementType.IsValueType();

        /// <summary>
        /// Gets a value indicating whether this field is an object reference.
        /// </summary>
        /// <returns>True if this field is an object reference, false otherwise.</returns>
        public virtual bool IsObjectReference => ElementType.IsObjectReference();

        /// <summary>
        /// Gets the size of this field.
        /// </summary>
        public int Size => GetSize(Type, ElementType);

        /// <summary>
        /// Attributes of this field;
        /// </summary>
        public abstract FieldAttributes Attributes { get; }

        /// <summary>
        /// For instance fields, this is the offset of the field within the object.
        /// For static fields this is the offset within the block of memory allocated for the module's static fields.
        /// </summary>
        public abstract int Offset { get; }

        /// <summary>
        /// Returns a string representation of this object.
        /// </summary>
        /// <returns>A string representation of this object.</returns>
        public override string? ToString()
        {
            ClrType? type = Type;
            if (type is null)
                return Name;

            return $"{type.Name} {Name}";
        }

        internal static int GetSize(ClrType? type, ClrElementType cet)
        {
            // todo:  What if we have a struct which is not fully constructed (null MT,
            //        null type) and need to get the size of the field?
            switch (cet)
            {
                case ClrElementType.Struct:
                    if (type is null)
                        return 1;

                    ClrField? last = null;
                    foreach (ClrField field in type.Fields)
                    {
                        if (last is null)
                            last = field;
                        else if (field.Offset > last.Offset)
                            last = field;
                        else if (field.Offset == last.Offset && field.Size > last.Size)
                            last = field;
                    }

                    if (last is null)
                        return 0;

                    return last.Offset + last.Size;

                case ClrElementType.Int8:
                case ClrElementType.UInt8:
                case ClrElementType.Boolean:
                    return 1;

                case ClrElementType.Float:
                case ClrElementType.Int32:
                case ClrElementType.UInt32:
                    return 4;

                case ClrElementType.Double: // double
                case ClrElementType.Int64:
                case ClrElementType.UInt64:
                    return 8;

                case ClrElementType.String:
                case ClrElementType.Class:
                case ClrElementType.Array:
                case ClrElementType.SZArray:
                case ClrElementType.Object:
                case ClrElementType.NativeInt: // native int
                case ClrElementType.NativeUInt: // native unsigned int
                case ClrElementType.Pointer:
                case ClrElementType.FunctionPointer:
                    return IntPtr.Size;

                case ClrElementType.UInt16:
                case ClrElementType.Int16:
                case ClrElementType.Char: // u2
                    return 2;
            }

            throw new Exception("Unexpected element type.");
        }
    }
}