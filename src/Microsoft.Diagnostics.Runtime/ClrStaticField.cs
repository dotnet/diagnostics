// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a static field in the target process.
    /// </summary>
    public sealed class ClrStaticField : ClrField, IClrStaticField
    {
        private readonly IClrFieldHelpers _helpers;
        private string? _name;
        private ClrType? _type;
        private FieldAttributes _attributes = FieldAttributes.ReservedMask;

        internal ClrStaticField(ClrType containingType, ClrType? type, IClrFieldHelpers helpers, in FieldData data)
        {
            if (containingType is null)
                throw new ArgumentNullException(nameof(containingType));

            ContainingType = containingType;
            _type = type;
            _helpers = helpers;
            Token = (int)data.FieldToken;
            ElementType = (ClrElementType)data.ElementType;
            Offset = (int)data.Offset;
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

        public override ClrType Type
        {
            get
            {
                if (_type != null)
                    return _type;

                InitData();
                return _type!;
            }
        }

        public override int Token { get; }

        public override int Offset { get; }

        public override ClrType ContainingType { get; }

        public override FieldAttributes Attributes
        {
            get
            {
                InitData();
                return _attributes;
            }
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
                    _name = string.Intern(name);
                else if (options == StringCaching.Cache)
                    _name = name;
            }

            // We may have to try to construct a type from the sigParser if the method table was a bust in the constructor
            if (_type == null)
            {
            }

            return name;
        }

        /// <summary>
        /// Returns whether this static field has been initialized in a particular AppDomain
        /// or not.  If a static variable has not been initialized, then its class constructor
        /// may have not been run yet.  Calling any of the Read* methods on an uninitialized static
        /// will result in returning either NULL or a value of 0.
        /// </summary>
        /// <param name="appDomain">The AppDomain to see if the variable has been initialized.</param>
        /// <returns>
        /// True if the field has been initialized (even if initialized to NULL or a default
        /// value), false if the runtime has not initialized this variable.
        /// </returns>
        public bool IsInitialized(ClrAppDomain appDomain) => GetAddress(appDomain) != 0;


        bool IClrStaticField.IsInitialized(IClrAppDomain appDomain) => GetAddress(appDomain) != 0;

        /// <summary>
        /// Gets the address of the static field's value in memory.
        /// </summary>
        /// <returns>The address of the field's value.</returns>
        public ulong GetAddress(ClrAppDomain appDomain) => _helpers.GetStaticFieldAddress(this, appDomain.Address);

        public ulong GetAddress(IClrAppDomain appDomain) => _helpers.GetStaticFieldAddress(this, appDomain.Address);

        /// <summary>
        /// Reads the value of the field as an unmanaged struct or primitive type.
        /// </summary>
        /// <typeparam name="T">An unmanaged struct or primitive type.</typeparam>
        /// <returns>The value read.</returns>
        public T Read<T>(ClrAppDomain appDomain) where T : unmanaged
        {
            ulong address = GetAddress(appDomain);
            if (address == 0)
                return default;

            if (!_helpers.DataReader.Read(address, out T value))
                return default;

            return value;
        }

        T IClrStaticField.Read<T>(IClrAppDomain appDomain)
        {
            ulong address = GetAddress(appDomain);
            if (address == 0)
                return default;

            if (!_helpers.DataReader.Read(address, out T value))
                return default;

            return value;
        }

        /// <summary>
        /// Reads the value of an object field.
        /// </summary>
        /// <returns>The value read.</returns>
        public ClrObject ReadObject(ClrAppDomain appDomain)
        {
            ulong address = GetAddress(appDomain);
            if (address == 0 || !_helpers.DataReader.ReadPointer(address, out ulong obj) || obj == 0)
                return default;

            return ContainingType.Heap.GetObject(obj);
        }

        IClrValue IClrStaticField.ReadObject(IClrAppDomain appDomain)
        {
            ulong address = GetAddress(appDomain);
            if (address == 0 || !_helpers.DataReader.ReadPointer(address, out ulong obj) || obj == 0)
                return default(ClrObject);

            return ContainingType.Heap.GetObject(obj);
        }

        /// <summary>
        /// Reads a ValueType struct from the instance field.
        /// </summary>
        /// <returns>The value read.</returns>
        public ClrValueType ReadStruct(ClrAppDomain appDomain)
        {
            ulong address = GetAddress(appDomain);
            if (address == 0)
                return default;

            return new ClrValueType(address, Type, interior: true);
        }

        IClrValue IClrStaticField.ReadStruct(IClrAppDomain appDomain)
        {
            ulong address = GetAddress(appDomain);
            if (address == 0)
                return default(ClrValueType);

            return new ClrValueType(address, Type, interior: true);
        }

        /// <summary>
        /// Reads a string from the instance field.
        /// </summary>
        /// <returns>The value read.</returns>
        public string? ReadString(ClrAppDomain appDomain)
        {
            ClrObject obj = ReadObject(appDomain);
            if (obj.IsNull)
                return null;

            return obj.AsString();
        }

        string? IClrStaticField.ReadString(IClrAppDomain appDomain)
        {
            IClrStaticField field = this;
            IClrValue obj = field.ReadObject(appDomain);
            if (obj.IsNull)
                return null;

            return obj.AsString();
        }
    }
}
