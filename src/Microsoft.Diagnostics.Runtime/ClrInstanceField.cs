// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents an instance field of a type.   Fundamentally it represents a name and a type
    /// </summary>
    public sealed class ClrInstanceField : ClrField, IClrInstanceField
    {
        private FieldAttributes _attributes = FieldAttributes.ReservedMask;

        private readonly IClrFieldHelpers _helpers;
        private string? _name;
        private ClrType? _type;

        internal ClrInstanceField(ClrType containingType, ClrType? type, IClrFieldHelpers helpers, in FieldData data)
        {
            if (containingType is null)
                throw new ArgumentNullException(nameof(containingType));

            ContainingType = containingType;
            Token = (int)data.FieldToken;
            ElementType = (ClrElementType)data.ElementType;
            Offset = (int)data.Offset;

            _helpers = helpers;

            // Must be the last use of 'data' in this constructor.
            _type = type;
            if (ElementType == ClrElementType.Class && _type != null)
                ElementType = _type.ElementType;

            DebugOnlyLoadLazyValues();
        }

        [Conditional("DEBUG")]
        private void DebugOnlyLoadLazyValues()
        {
            InitData();
        }

        private void InitData()
        {
            if (_attributes != FieldAttributes.ReservedMask)
                return;

            ReadData();
        }

        private string? ReadData()
        {
            if (!_helpers.ReadProperties(ContainingType, Token, out string? name, out _attributes, ref _type))
                return null;

            StringCaching options = ContainingType.Heap.Runtime.DataTarget?.CacheOptions.CacheFieldNames ?? StringCaching.Cache;
            if (name != null)
            {
                if (options == StringCaching.Intern)
                    name = string.Intern(name);

                if (options != StringCaching.None)
                    _name = name;
            }

            return name;
        }

        public override FieldAttributes Attributes
        {
            get
            {
                InitData();
                return _attributes;
            }
        }

        public override ClrElementType ElementType { get; }
        public override bool IsObjectReference => ElementType.IsObjectReference();
        public override bool IsValueType => ElementType.IsValueType();
        public override bool IsPrimitive => ElementType.IsPrimitive();

        public override string? Name
        {
            get
            {
                if (_name != null)
                    return _name;

                return ReadData();
            }
        }

        public override ClrType? Type
        {
            get
            {
                if (_type != null)
                    return _type;

                InitData();
                return _type;
            }
        }

        public override int Token { get; }

        public override int Offset { get; }

        public override ClrType ContainingType { get; }

        /// <summary>
        /// Reads the value of the field as an unmanaged struct or primitive type.
        /// </summary>
        /// <typeparam name="T">An unmanaged struct or primitive type.</typeparam>
        /// <param name="objRef">The object to read the instance field from.</param>
        /// <param name="interior">Whether or not the field is interior to a struct.</param>
        /// <returns>The value read.</returns>
        public T Read<T>(ulong objRef, bool interior) where T : unmanaged
        {
            ulong address = GetAddress(objRef, interior);
            if (address == 0)
                return default;

            if (!_helpers.DataReader.Read(address, out T value))
                return default;

            return value;
        }

        /// <summary>
        /// Reads the value of an object field.
        /// </summary>
        /// <param name="objRef">The object to read the instance field from.</param>
        /// <param name="interior">Whether or not the field is interior to a struct.</param>
        /// <returns>The value read.</returns>
        public ClrObject ReadObject(ulong objRef, bool interior)
        {
            ulong address = GetAddress(objRef, interior);
            if (address == 0 || !_helpers.DataReader.ReadPointer(address, out ulong obj) || obj == 0)
                return default;

            return ContainingType.Heap.GetObject(obj);
        }

        IClrValue IClrInstanceField.ReadObject(ulong objRef, bool interior) => ReadObject(objRef, interior);

        /// <summary>
        /// Reads a ValueType struct from the instance field.
        /// </summary>
        /// <param name="objRef">The object to read the instance field from.</param>
        /// <param name="interior">Whether or not the field is interior to a struct.</param>
        /// <returns>The value read.</returns>
        public ClrValueType ReadStruct(ulong objRef, bool interior)
        {
            ulong address = GetAddress(objRef, interior);
            if (address == 0)
                return default;

            return new ClrValueType(address, Type, interior: true);
        }

        IClrValue IClrInstanceField.ReadStruct(ulong objRef, bool interior) => ReadStruct(objRef, interior);

        /// <summary>
        /// Reads a string from the instance field.
        /// </summary>
        /// <param name="objRef">The object to read the instance field from.</param>
        /// <param name="interior">Whether or not the field is interior to a struct.</param>
        /// <returns>The value read.</returns>
        public string? ReadString(ulong objRef, bool interior)
        {
            ClrObject obj = ReadObject(objRef, interior);
            if (obj.IsNull)
                return null;

            return obj.AsString();
        }

        /// <summary>
        /// Returns the address of the value of this field.  Equivalent to GetFieldAddress(objRef, false).
        /// </summary>
        /// <param name="objRef">The object to get the field address for.</param>
        /// <returns>The value of the field.</returns>
        public ulong GetAddress(ulong objRef)
        {
            return GetAddress(objRef, false);
        }

        /// <summary>
        /// Returns the address of the value of this field.  Equivalent to GetFieldAddress(objRef, false).
        /// </summary>
        /// <param name="objRef">The object to get the field address for.</param>
        /// <param name="interior">
        /// Whether the enclosing type of this field is a value class,
        /// and that value class is embedded in another object.
        /// </param>
        /// <returns>The value of the field.</returns>
        public ulong GetAddress(ulong objRef, bool interior)
        {
            if (interior)
                return objRef + (ulong)Offset;

            return objRef + (ulong)(Offset + IntPtr.Size);
        }
    }
}