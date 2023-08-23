// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents an instance of a type which inherits from <see cref="ValueType"/>.
    /// </summary>
    public readonly struct ClrValueType : IClrValue
    {
        private IDataReader DataReader => GetTypeOrThrow().Helpers.DataReader;
        private readonly bool _interior;

        /// <summary>
        /// Gets the address of the object.
        /// </summary>
        public ulong Address { get; }

        /// <summary>
        /// Gets the type of the object.
        /// </summary>
        public ClrType? Type { get; }

        /// <summary>
        /// Returns whether this ClrValueType has a valid Type.  In most normal operations of ClrMD, we will have a
        /// non-null type.  However if we are missing metadata, or in some generic cases we might not be able to
        /// determine the type of this value type.  In those cases, Type? will be null and IsValid will return false.
        /// </summary>
        public bool IsValid => Type != null;

        internal ClrValueType(ulong address, ClrType? type, bool interior)
        {
            Address = address;
            Type = type;
            _interior = interior;

            DebugOnly.Assert(type != null && type.IsValueType);
        }

        public bool Equals(ClrObject obj) => false;
        public bool Equals(ClrValueType other) => other.Address == Address;
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is null)
                return false;

            if (obj is IClrValue other)
                return other.Equals(this);

            return false;
        }

        public override int GetHashCode() => Address.GetHashCode();

        /// <summary>
        /// Gets the given object reference field from this ClrObject.
        /// </summary>
        /// <param name="fieldName">The name of the field to retrieve.</param>
        /// <returns>A ClrObject of the given field.</returns>
        /// <exception cref="ArgumentException">
        /// The given field does not exist in the object.
        /// -or-
        /// The given field was not an object reference.
        /// </exception>
        public ClrObject ReadObjectField(string fieldName)
        {
            ClrType type = GetTypeOrThrow();
            ClrInstanceField field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            if (!field.IsObjectReference)
                throw new ArgumentException($"Field '{type.Name}.{fieldName}' is not an object reference.");

            ClrHeap heap = type.Heap;

            ulong addr = field.GetAddress(Address, _interior);
            if (!DataReader.ReadPointer(addr, out ulong obj))
                return default;

            return heap.GetObject(obj);
        }

        /// <summary>
        /// Gets the value of a primitive field.  This will throw an InvalidCastException if the type parameter
        /// does not match the field's type.
        /// </summary>
        /// <typeparam name="T">The type of the field itself.</typeparam>
        /// <param name="fieldName">The name of the field.</param>
        /// <returns>The value of this field.</returns>
        public T ReadField<T>(string fieldName)
            where T : unmanaged
        {
            ClrType type = GetTypeOrThrow();
            ClrInstanceField field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            object value = field.Read<T>(Address, _interior);
            return (T)value;
        }

        /// <summary>
        /// </summary>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        public ClrValueType ReadValueTypeField(string fieldName)
        {
            ClrType type = GetTypeOrThrow();
            ClrInstanceField? field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");

            if (!field.IsValueType)
                throw new ArgumentException($"Field '{type.Name}.{fieldName}' is not a ValueClass.");

            if (field.Type is null)
                throw new InvalidOperationException("Field does not have an associated class.");

            ulong addr = field.GetAddress(Address, _interior);
            return new ClrValueType(addr, field.Type, true);
        }

        /// <summary>
        /// Gets a string field from the object.  Note that the type must match exactly, as this method
        /// will not do type coercion.
        /// </summary>
        /// <param name="fieldName">The name of the field to get the value for.</param>
        /// <param name="maxLength">The maximum length of the string returned.  Warning: If the DataTarget
        /// being inspected has corrupted or an inconsistent heap state, the length of a string may be
        /// incorrect, leading to OutOfMemory and other failures.</param>
        /// <returns>The value of the given field.</returns>
        /// <exception cref="ArgumentException">No field matches the given name.</exception>
        /// <exception cref="InvalidOperationException">The field is not a string.</exception>
        public string? ReadStringField(string fieldName, int maxLength = 4096)
        {
            ulong address = GetFieldAddress(fieldName, ClrElementType.String, "string");
            if (!DataReader.ReadPointer(address, out ulong str))
                return null;

            if (str == 0)
                return null;

            ClrObject obj = new(str, GetTypeOrThrow().Heap.StringType);
            return obj.AsString(maxLength);
        }

        /// <summary>
        /// Tries to obtain the given string field from this ClrObject.  Returns false if the field wasn't found or if
        /// the underlying type was not a string..
        /// </summary>
        /// <param name="fieldName">The name of the field to retrieve.</param>
        /// <param name="maxLength">The string max length or the default.</param>
        /// <param name="result">True if the field was found and the field's type is a string.  Returns false otherwise.</param>
        /// <returns>A string of the given field.</returns>
        public bool TryReadStringField(string fieldName, int? maxLength, out string? result)
        {
            result = default;
            if (fieldName is null)
                return false;

            ClrType? type = Type;
            if (type is null)
                return false;

            ClrInstanceField? field = type.GetFieldByName(fieldName);
            if (field is null)
                return false;

            if (field.ElementType != ClrElementType.String)
                return false;

            ulong addr = field.GetAddress(Address);
            IDataReader dataReader = type.Helpers.DataReader;
            if (!dataReader.ReadPointer(addr, out ulong strPtr))
                return false;

            if (strPtr == 0)
                return false;

            ClrObject obj = new(strPtr, GetTypeOrThrow().Heap.StringType);
            result = obj.AsString(maxLength ?? 1024);

            return true;
        }

        private ulong GetFieldAddress(string fieldName, ClrElementType element, string typeName)
        {
            ClrType type = GetTypeOrThrow();
            ClrInstanceField field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            if (field.ElementType != element)
                throw new InvalidOperationException($"Field '{type.Name}.{fieldName}' is not of type '{typeName}'.");

            ulong address = field.GetAddress(Address, _interior);
            return address;
        }

        public bool Equals(IClrValue? other)
        {
            return other != null && Address == other.Address && Type == (ClrType?)other.Type;
        }


        private ClrType GetTypeOrThrow()
        {
            if (Type is null)
                throw new InvalidOperationException($"Unknown type of value at {Address:x}.");

            return Type;
        }

        public bool ContainsPointers => Type?.ContainsPointers ?? false;
        public ulong Size => (ulong)(Type?.StaticSize ?? 0);

        bool IClrValue.HasComCallableWrapper => false;

        bool IClrValue.HasRuntimeCallableWrapper => false;

        bool IClrValue.IsArray => false;

        bool IClrValue.IsBoxedValue => false;

        bool IClrValue.IsComClassFactory => false;

        bool IClrValue.IsDelegate => false;

        bool IClrValue.IsException => false;

        bool IClrValue.IsFree => false;

        bool IClrValue.IsNull => false;

        bool IClrValue.IsRuntimeType => false;

        SyncBlock? IClrValue.SyncBlock => null;

        IClrType? IClrValue.Type => Type;

        IClrArray IClrValue.AsArray()
        {
            throw new InvalidOperationException($"Object {Address:x} is not an array, type is '{Type?.Name}'.");
        }

        IClrDelegate IClrValue.AsDelegate()
        {
            throw new InvalidOperationException($"Object {Address:x} is not a delegate, type is '{Type?.Name}'.");
        }

        IClrException? IClrValue.AsException() => null;

        IClrType? IClrValue.AsRuntimeType() => null;

        string? IClrValue.AsString(int maxLength) => null;

        IEnumerable<ulong> IClrValue.EnumerateReferenceAddresses(bool carefully, bool considerDependantHandles)
        {
            // todo
            throw new NotImplementedException();
        }

        IEnumerable<IClrValue> IClrValue.EnumerateReferences(bool carefully, bool considerDependantHandles)
        {
            // todo
            throw new NotImplementedException();
        }

        IEnumerable<IClrReference> IClrValue.EnumerateReferencesWithFields(bool carefully, bool considerDependantHandles)
        {
            // todo
            throw new NotImplementedException();
        }

        IComCallableWrapper? IClrValue.GetComCallableWrapper() => null;

        IRuntimeCallableWrapper? IClrValue.GetRuntimeCallableWrapper() => null;

        T IClrValue.ReadBoxedValue<T>()
        {
            IClrTypeHelpers? helpers = Type?.Helpers;
            if (helpers is null)
                return default;

            return helpers.DataReader.Read<T>(Address);
        }

        IClrValue IClrValue.ReadObjectField(string fieldName) => ReadObjectField(fieldName);

        bool IClrValue.TryReadField<T>(string fieldName, out T result)
        {
            ClrInstanceField? field = Type?.GetFieldByName(fieldName);
            if (field is null)
            {
                result = default;
                return false;
            }

            result = field.Read<T>(Address, _interior);
            return true;
        }

        bool IClrValue.TryReadObjectField(string fieldName, [NotNullWhen(true)] out IClrValue? result)
        {
            result = null;

            if (Type is null)
                return false;

            ClrInstanceField? field = Type.GetFieldByName(fieldName);
            if (field is null || !field.IsObjectReference)
                return false;

            ClrHeap heap = Type.Heap;

            ulong addr = field.GetAddress(Address, _interior);
            if (!DataReader.ReadPointer(addr, out ulong obj))
                return false;

            result = heap.GetObject(obj);
            return true;
        }

        bool IClrValue.TryReadValueTypeField(string fieldName, [NotNullWhen(true)] out IClrValue? result)
        {
            ClrInstanceField? field = Type?.GetFieldByName(fieldName);
            if (field is null || field.IsValueType || field.Type is null)
            {
                result = null;
                return false;
            }

            ulong addr = field.GetAddress(Address, _interior);
            result = new ClrValueType(addr, field.Type, true);
            return true;
        }

        IClrValue IClrValue.ReadValueTypeField(string fieldName) => ReadValueTypeField(fieldName);

        public static bool operator ==(ClrValueType left, ClrValueType right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ClrValueType left, ClrValueType right)
        {
            return !(left == right);
        }
    }
}
