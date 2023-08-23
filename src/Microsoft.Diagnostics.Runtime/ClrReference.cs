// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    public readonly struct ClrReference : IClrReference
    {
        private const ulong OffsetFlag = 0x8000000000000000ul;
        private const ulong DependentFlag = 0x4000000000000000ul;

        private readonly ulong _offsetOrHandle;

        /// <summary>
        /// The object that <see cref="Field"/> contained.
        /// </summary>
        public ClrObject Object { get; }

        /// <summary>
        /// The offset into the containing object this address is found at.  Only valid if <see cref="IsField"/> is true.
        /// </summary>
        public int Offset
        {
            get
            {
                if ((_offsetOrHandle & OffsetFlag) == OffsetFlag)
                {
                    unchecked
                    {
                        // The (uint) cast will slice off the high bits
                        return (int)(uint)_offsetOrHandle;
                    }
                }

                return -1;
            }
        }

        /// <summary>
        /// Resolves the inner field reference for value types.
        /// </summary>
        public ClrReference? InnerField
        {
            get
            {
                if (!IsField || Field?.Type == null || !Field.Type.IsValueType)
                    return null;

                int offset = Offset - Field.Offset;

                ClrInstanceField? field = FindField(Field.Type.Fields, offset);
                if (field is null)
                    return null;

                // Primitive types intentionally have a recursive definition.  In the case where we incorrectly find
                // an object's offset as one of these fields we need to break out of the infinite loop.
                if (field == Field && field.Name == "m_value")
                    return null;

                unchecked
                {
                    return new ClrReference(Object, field, OffsetFlag | (uint)offset);
                }
            }
        }

        /// <summary>
        /// The field this object was contained in.  This property may be null if this reference came from
        /// a DependentHandle or if the reference came from an array entry.
        /// Only valid to call if <see cref="IsField"/> is true.
        /// </summary>
        public ClrInstanceField? Field { get; }

        /// <summary>
        /// Returns true if this reference came from a dependent handle.
        /// </summary>
        public bool IsDependentHandle => (_offsetOrHandle & DependentFlag) == DependentFlag;

        /// <summary>
        /// Returns true if this reference came from a field in another object.
        /// </summary>
        public bool IsField => (_offsetOrHandle & OffsetFlag) == OffsetFlag && Field != null;

        /// <summary>
        /// Returns true if this reference came from an entry in an array.
        /// </summary>
        public bool IsArrayElement => (_offsetOrHandle & OffsetFlag) == OffsetFlag && Field == null;

        IClrInstanceField? IClrReference.Field => Field;

        IClrReference? IClrReference.InnerField => InnerField;

        IClrValue IClrReference.Object => Object;

        /// <summary>
        /// Create a field reference from a dependent handle value.  We do not keep track of the dependent handle it came from
        /// so we don't accept the value here.
        /// </summary>
        /// <param name="reference">The object referenced.</param>
        public static ClrReference CreateFromDependentHandle(ClrObject reference) => new(reference, null, DependentFlag);

        /// <summary>
        /// Creates a ClrFieldReference from an actual field.
        /// </summary>
        /// <param name="reference">The object referenced.</param>
        /// <param name="containingType">The type of the object which points to <paramref name="reference"/>.</param>
        /// <param name="offset">The offset within the source object where <paramref name="reference"/> was located.  This offset
        /// should start from where the object's data starts (IE this offset should NOT contain the MethodTable in the offset
        /// calculation.</param>
        public static ClrReference CreateFromFieldOrArray(ClrObject reference, ClrType containingType, int offset)
        {
            if (containingType == null)
                throw new ArgumentNullException(nameof(containingType));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), $"{nameof(offset)} must be >= 0.");

            ClrInstanceField? field = FindField(containingType.Fields, offset);

            unchecked
            {
                return new ClrReference(reference, field, OffsetFlag | (uint)offset);
            }
        }

        private static ClrInstanceField? FindField(ImmutableArray<ClrInstanceField> fields, int offset)
        {
            ClrInstanceField? field = null;
            foreach (ClrInstanceField curr in fields)
            {
                // If we found the correct field, stop searching
                if (curr.Offset <= offset && offset < curr.Offset + curr.Size)
                {
                    field = curr;
                    break;
                }

                // Sometimes .Size == 0 if we failed to properly determine the type of the field,
                // instead search for the field closest to the offset we are searching for.
                if (curr.Offset <= offset)
                {
                    if (field == null)
                        field = curr;
                    else if (field.Offset < curr.Offset)
                        field = curr;
                }
            }

            return field;
        }

        private ClrReference(ClrObject obj, ClrInstanceField? field, ulong offsetOrHandleValue)
        {
            _offsetOrHandle = offsetOrHandleValue;
            Object = obj;
            Field = field;
        }

        public override string ToString()
        {
            if (IsField)
            {
                StringBuilder sb = new();
                sb.Append(Field?.Name);

                ClrReference? inner = InnerField;
                while (inner is ClrReference r)
                {
                    sb.Append('.');
                    sb.Append(r.Field?.Name);

                    inner = r.InnerField;
                }

                sb.Append($" = ");

                sb.Append($"{Object.Address:x12} {Object.Type?.Name ?? "error"}");

                return sb.ToString();
            }

            if (IsDependentHandle)
                return $"{Object.Address:x12} {Object.Type?.Name ?? "error"} (dependent handle)";

            return $"{Object.Address:x12} {Object.Type?.Name ?? "error"}";
        }
    }
}
