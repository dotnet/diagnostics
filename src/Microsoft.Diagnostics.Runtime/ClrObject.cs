// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents an object in the target process.
    /// </summary>
    public readonly struct ClrObject : IClrValue, IEquatable<ClrObject>
    {
        internal const string RuntimeTypeName = "System.RuntimeType";

        private IClrTypeHelpers Helpers => GetTypeOrThrow().Helpers;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="address">The address of the object.</param>
        /// <param name="type">The concrete type of the object.</param>
        public ClrObject(ulong address, ClrType? type)
        {
            Address = address;
            Type = type;
        }

        /// <summary>
        /// Enumerates all objects that this object references.
        /// </summary>
        /// <param name="carefully">Only returns pointers which lie on the managed heap.  In very rare cases it's possible to
        /// create a crash dump where the GC was in the middle of updating data structures, or to create a crash dump of a process
        /// with heap corruption.  In those cases, setting carefully=true would ensure we would not enumerate those bad references.
        /// Note that setting carefully=true will cause a small performance penalty.</param>
        /// <param name="considerDependantHandles">Setting this to true will have ClrMD check for dependent handle references.
        /// Checking dependent handles does come at a performance penalty but will give you the true reference chain as the
        /// GC sees it.</param>
        /// <returns>An enumeration of object references.</returns>
        public IEnumerable<ClrObject> EnumerateReferences(bool carefully = false, bool considerDependantHandles = true)
        {
            if (Type is null)
                return Enumerable.Empty<ClrObject>();

            return Type.Heap.EnumerateObjectReferences(Address, Type, carefully, considerDependantHandles);
        }

        /// <summary>
        /// Enumerates all objects that this object references.  This method also enumerates the field (or handle) that this
        /// reference comes from.
        /// </summary>
        /// <param name="carefully">Only returns pointers which lie on the managed heap.  In very rare cases it's possible to
        /// create a crash dump where the GC was in the middle of updating data structures, or to create a crash dump of a process
        /// with heap corruption.  In those cases, setting carefully=true would ensure we would not enumerate those bad references.
        /// Note that setting carefully=true will cause a small performance penalty.</param>
        /// <param name="considerDependantHandles">Setting this to true will have ClrMD check for dependent handle references.
        /// Checking dependent handles does come at a performance penalty but will give you the true reference chain as the
        /// GC sees it.</param>
        /// <returns>An enumeration of object references.</returns>
        public IEnumerable<ClrReference> EnumerateReferencesWithFields(bool carefully = false, bool considerDependantHandles = true)
        {
            if (Type is null)
                return Enumerable.Empty<ClrReference>();

            return Type.Heap.EnumerateReferencesWithFields(Address, Type, carefully, considerDependantHandles);
        }

        /// <summary>
        /// Enumerates all objects that this object references.
        /// </summary>
        /// <param name="carefully">Only returns pointers which lie on the managed heap.  In very rare cases it's possible to
        /// create a crash dump where the GC was in the middle of updating data structures, or to create a crash dump of a process
        /// with heap corruption.  In those cases, setting carefully=true would ensure we would not enumerate those bad references.
        /// Note that setting carefully=true will cause a small performance penalty.</param>
        /// <param name="considerDependantHandles">Setting this to true will have ClrMD check for dependent handle references.
        /// Checking dependent handles does come at a performance penalty but will give you the true reference chain as the
        /// GC sees it.</param>
        /// <returns>An enumeration of object references.</returns>
        public IEnumerable<ulong> EnumerateReferenceAddresses(bool carefully = false, bool considerDependantHandles = true)
        {
            if (Type is null)
                return Enumerable.Empty<ulong>();

            return Type.Heap.EnumerateReferenceAddresses(Address, Type, carefully, considerDependantHandles);
        }

        /// <summary>
        /// Returns true if this object is a boxed struct or primitive type that
        /// </summary>
        public bool IsBoxedValue => Type != null && (Type.IsPrimitive || Type.IsValueType);

        /// <summary>
        /// Reads a boxed primitive value.
        /// </summary>
        /// <typeparam name="T">An unmanaged struct or primitive type to read out of the object.</typeparam>
        /// <returns>The value read.</returns>
        public T ReadBoxedValue<T>() where T : unmanaged
        {
            IClrTypeHelpers? helpers = Helpers;
            if (helpers is null)
                return default;

            return helpers.DataReader.Read<T>(Address + (ulong)IntPtr.Size);
        }

        public bool IsException => Type != null && Type.IsException;

        public ClrException? AsException()
        {
            if (!IsException)
                throw new InvalidOperationException($"Object {Address:x} is not an Exception.");

            if (Type is null || !Type.IsException)
                return null;

            return new ClrException(Helpers, null, this);
        }

        /// <summary>
        /// Gets the address of the object.
        /// </summary>
        public ulong Address { get; }

        /// <summary>
        /// Gets the type of the object.
        /// </summary>
        public ClrType? Type { get; }

        IClrType? IClrValue.Type => Type;

        /// <summary>
        /// Returns whether this is free space on the GC heap and not a real object.
        /// </summary>
        public bool IsFree => Type?.IsFree ?? false;

        /// <summary>
        /// Returns whether this is a valid object.  This will return null
        /// </summary>
        public bool IsValid => Address != 0 && Type != null;

        /// <summary>
        /// Returns if the object value is <see langword="null"/>.
        /// </summary>
        public bool IsNull => Address == 0;

        /// <summary>
        /// Gets the size of the object.
        /// </summary>
        public ulong Size => GetTypeOrThrow().Heap.GetObjectSize(Address, GetTypeOrThrow());

        /// <summary>
        /// Obtains the SyncBlock for this object.  Returns null if there is no SyncBlock associated with this object.
        /// </summary>
        public SyncBlock? SyncBlock => Type?.Heap.GetSyncBlock(Address);

        public ClrThinLock? GetThinLock() => Type?.Heap.GetThinlock(Address);

        /// <summary>
        /// Returns true if this object is a COM class factory.
        /// </summary>
        public bool IsComClassFactory => (GetTypeOrThrow().Heap.GetComFlags(Address) & SyncBlockComFlags.ComClassFactory) == SyncBlockComFlags.ComClassFactory;

        /// <summary>
        /// Returns true if this object is a ComCallableWrapper.
        /// </summary>
        public bool HasComCallableWrapper => (GetTypeOrThrow().Heap.GetComFlags(Address) & SyncBlockComFlags.ComCallableWrapper) == SyncBlockComFlags.ComCallableWrapper;

        /// <summary>
        /// Returns true if this object is a RuntimeCallableWrapper.
        /// </summary>
        public bool HasRuntimeCallableWrapper => (GetTypeOrThrow().Heap.GetComFlags(Address) & SyncBlockComFlags.RuntimeCallableWrapper) == SyncBlockComFlags.RuntimeCallableWrapper;

        /// <summary>
        /// Returns the ComCallableWrapper for the given object.
        /// </summary>
        /// <returns>The ComCallableWrapper associated with the object, <see langword="null"/> if obj is not a CCW.</returns>
        public ComCallableWrapper? GetComCallableWrapper()
        {
            if (IsNull || !IsValid || !HasComCallableWrapper)
                return null;

            return Helpers.CreateCCWForObject(Address);
        }

        /// <summary>
        /// Returns the RuntimeCallableWrapper for the given object.
        /// </summary>
        /// <returns>The RuntimeCallableWrapper associated with the object, <see langword="null"/> if obj is not a RCW.</returns>
        public RuntimeCallableWrapper? GetRuntimeCallableWrapper()
        {
            if (IsNull || !IsValid)
                return null;

            return Helpers.CreateRCWForObject(Address);
        }

        /// <summary>
        /// Gets a value indicating whether this object possibly contains GC pointers.
        /// </summary>
        public bool ContainsPointers => Type != null && Type.ContainsPointers;

        /// <summary>
        /// Gets a value indicating whether this object is an array.
        /// </summary>
        public bool IsArray => GetTypeOrThrow().IsArray;

        /// <summary>
        /// returns the object as an array if the object has array type.
        /// </summary>
        /// <returns></returns>
        public ClrArray AsArray()
        {
            ClrType type = GetTypeOrThrow();
            if (!type.IsArray)
                throw new InvalidOperationException($"Object {Address:x} is not an array, type is '{type.Name}'.");

            return new ClrArray(Address, type);
        }

        IClrArray IClrValue.AsArray() => AsArray();

        /// <summary>
        /// Converts a ClrObject into its string value.
        /// </summary>
        /// <param name="obj">A string object.</param>
        public static explicit operator string?(ClrObject obj) => obj.AsString();

        /// <summary>
        /// Returns <see cref="Address"/> sweetening obj to pointer move.
        /// <Para>Example: ulong address = clrObject</Para>
        /// </summary>
        /// <param name="clrObject">An object to get address of.</param>
        public static implicit operator ulong(ClrObject clrObject) => clrObject.Address;

        /// <summary>
        /// Tries to obtain the given object field from this ClrObject.  Returns false if the field wasn't found or if
        /// the underlying type was not an object.
        /// </summary>
        /// <param name="fieldName">The name of the field to retrieve.</param>
        /// <param name="result">True if the field was found and the field's type is an object.  Returns false otherwise.</param>
        /// <returns>A ClrObject of the given field.</returns>
        public bool TryReadObjectField(string fieldName, out ClrObject result)
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

            if (!field.IsObjectReference)
                return false;

            ClrHeap heap = type.Heap;

            ulong addr = field.GetAddress(Address);
            if (!type.Helpers.DataReader.ReadPointer(addr, out ulong obj))
                return false;

            result = heap.GetObject(obj);
            return true;
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

            result = Helpers.ReadString(strPtr, maxLength ?? 1024);
            return true;
        }

        /// <summary>
        /// Gets the given object reference field from this ClrObject.
        /// </summary>
        /// <param name="fieldName">The name of the field to retrieve.</param>
        /// <returns>A ClrObject of the given field.</returns>
        /// <exception cref="ArgumentException">The given field does not exist in the object.</exception>
        /// <exception cref="InvalidOperationException"><see cref="IsNull"/> is <see langword="true"/>.</exception>
        public ClrObject ReadObjectField(string fieldName)
        {
            ClrType type = GetTypeOrThrow();
            ClrInstanceField? field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            if (!field.IsObjectReference)
                throw new ArgumentException($"Field '{type.Name}.{fieldName}' is not an object reference.");

            ClrHeap heap = type.Heap;

            ulong addr = field.GetAddress(Address);
            if (!type.Helpers.DataReader.ReadPointer(addr, out ulong obj))
                return default;

            return heap.GetObject(obj);
        }

        public ClrValueType ReadValueTypeField(string fieldName)
        {
            ClrType type = GetTypeOrThrow();

            ClrInstanceField field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            if (!field.IsValueType)
                throw new ArgumentException($"Field '{type.Name}.{fieldName}' is not a ValueClass.");

            if (field.Type is null)
                throw new InvalidOperationException("Field does not have an associated class.");

            ulong addr = field.GetAddress(Address);
            return new ClrValueType(addr, field.Type, true);
        }

        public bool TryReadValueTypeField(string fieldName, out ClrValueType result)
        {
            result = default;

            ClrType? type = Type;
            if (type is null)
                return false;

            ClrInstanceField? field = type.GetFieldByName(fieldName);
            if (field is null)
                return false;

            if (!field.IsValueType)
                return false;

            if (field.Type is null)
                return false;

            ulong addr = field.GetAddress(Address);
            result = new ClrValueType(addr, field.Type, true);
            return true;
        }

        /// <summary>
        /// Gets the value of a primitive field.
        /// </summary>
        /// <typeparam name="T">The type of the field itself.</typeparam>
        /// <param name="fieldName">The name of the field.</param>
        /// <returns>The value of this field.</returns>
        public T ReadField<T>(string fieldName)
            where T : unmanaged
        {
            ClrType type = GetTypeOrThrow();
            ClrInstanceField field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            object value = field.Read<T>(Address, interior: false);
            return (T)value;
        }


        /// <summary>
        /// Attempts to read the value of a primitive field.  This method does no type checking on whether T
        /// matches the field's type.
        /// </summary>
        /// <typeparam name="T">The type of the field itself.</typeparam>
        /// <param name="fieldName">The name of the field.</param>
        /// <param name="result">The value of the missing field.</param>
        /// <returns>True if we obtained this field and read its value, false otherwise.</returns>
        public bool TryReadField<T>(string fieldName, out T result)
            where T : unmanaged
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

            result = field.Read<T>(Address, interior: false);
            return true;
        }

        public bool IsRuntimeType => Type?.Name == RuntimeTypeName;
        public ClrType? AsRuntimeType() => Helpers.CreateRuntimeType(this);

        /// <summary>
        /// Returns true if this object is a delegate, false otherwise.
        /// </summary>
        public bool IsDelegate
        {
            get
            {
                // Max depth = 8 should be enough to find a delegate type
                ClrType? type = Type;
                for (int i = 0; i < 8 && type != null; i++, type = type.BaseType)
                    if (type.Name == ClrDelegate.DelegateType)
                        return true;

                return false;
            }
        }

        /// <summary>
        /// Returns this object in a <see cref="ClrDelegate"/> view.  Note it is only valid to call
        /// <see cref="AsDelegate"/> if the underlying object is a subclass of System.Delegate.  You
        /// can check <see cref="IsDelegate"/> before calling <see cref="AsDelegate"/>, but that is
        /// not required as long as you are sure the object is a delegate or should be treated like
        /// one.
        /// </summary>
        /// <returns>Returns this object in a <see cref="ClrDelegate"/> view.</returns>
        public ClrDelegate AsDelegate()
        {
            if (!IsDelegate)
                throw new InvalidOperationException($"Object {Address:x} is not a delegate, type is '{Type?.Name}'.");

            return new ClrDelegate(this);
        }

        IClrDelegate IClrValue.AsDelegate() => AsDelegate();

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
        /// <exception cref="InvalidOperationException">
        /// The target object is <see langword="null"/> (that is, <see cref="IsNull"/> is <see langword="true"/>).
        /// -or-
        /// The field is not of the correct type.
        /// </exception>
        public string? ReadStringField(string fieldName, int maxLength = 4096)
        {
            ulong address = GetFieldAddress(fieldName, ClrElementType.String, "string");
            IDataReader dataReader = Helpers.DataReader;
            if (!dataReader.ReadPointer(address, out ulong strPtr))
                return null;

            if (strPtr == 0)
                return null;

            return Helpers.ReadString(strPtr, maxLength);
        }

        public string? AsString(int maxLength = 4096)
        {
            ClrType type = GetTypeOrThrow();
            if (!type.IsString)
                throw new InvalidOperationException($"Object {Address:x} is not a string, actual type: {Type?.Name ?? "null"}.");

            return Helpers.ReadString(Address, maxLength);
        }

        private ulong GetFieldAddress(string fieldName, ClrElementType element, string typeName)
        {
            ClrType type = GetTypeOrThrow();

            if (IsNull)
                throw new InvalidOperationException($"Cannot get field from null object.");

            ClrInstanceField field = type.GetFieldByName(fieldName) ?? throw new ArgumentException($"Type '{type.Name}' does not contain a field named '{fieldName}'");
            if (field.ElementType != element)
                throw new InvalidOperationException($"Field '{type.Name}.{fieldName}' is not of type '{typeName}'.");

            ulong address = field.GetAddress(Address);
            return address;
        }

        private ClrType GetTypeOrThrow()
        {
            if (IsNull)
                throw new InvalidOperationException("Object is null.");

            if (!IsValid)
                throw new InvalidOperationException($"Object {Address:x} is corrupted, could not determine type.");

            return Type!;
        }

        /// <summary>
        /// Determines if this instance and another specific <see cref="ClrObject" /> have the same value.
        /// <para>Instances are considered equal when they have same <see cref="Address" />.</para>
        /// </summary>
        /// <param name="other">The <see cref="ClrObject" /> to compare to this instance.</param>
        /// <returns><see langword="true"/> if the <see cref="Address" /> of the parameter is same as <see cref="Address" /> in this instance; <see langword="false"/> otherwise.</returns>
        public bool Equals(ClrObject other) => Address == other.Address;
        public bool Equals(ClrValueType other) => Address == other.Address;
        public bool Equals(IClrValue? other) => other is not null && other.Address == Address;
        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is IClrValue value)
                return value.Equals(this);

            return false;
        }

        /// <summary>
        /// Returns the hash code for this <see cref="ClrObject" /> based on its <see cref="Address" />.
        /// </summary>
        /// <returns>An <see cref="int" /> hash code for this instance.</returns>
        public override int GetHashCode()
        {
            return Address.GetHashCode();
        }

        /// <summary>
        /// Determines whether two specified <see cref="ClrObject" /> have the same value.
        /// </summary>
        /// <param name="left">First <see cref="ClrObject" /> to compare.</param>
        /// <param name="right">Second <see cref="ClrObject" /> to compare.</param>
        /// <returns><see langword="true"/> if <paramref name="left" /> <see cref="Equals(ClrObject)" /> <paramref name="right" />; <see langword="false"/> otherwise.</returns>
        public static bool operator ==(ClrObject left, ClrObject right) => left.Equals(right);

        /// <summary>
        /// Determines whether two specified <see cref="ClrObject" /> have different values.
        /// </summary>
        /// <param name="left">First <see cref="ClrObject" /> to compare.</param>
        /// <param name="right">Second <see cref="ClrObject" /> to compare.</param>
        /// <returns><see langword="true"/> if the value of <paramref name="left" /> is different from the value of <paramref name="right" />; <see langword="false"/> otherwise.</returns>
        public static bool operator !=(ClrObject left, ClrObject right) => !(left == right);

        /// <summary>
        /// ToString override.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"{Address:x} {Type?.Name}";
        }

        IClrException? IClrValue.AsException() => AsException();

        IClrType? IClrValue.AsRuntimeType() => AsRuntimeType();

        IEnumerable<IClrValue> IClrValue.EnumerateReferences(bool carefully, bool considerDependantHandles) => EnumerateReferences(carefully, considerDependantHandles).Cast<IClrValue>();
        IEnumerable<IClrReference> IClrValue.EnumerateReferencesWithFields(bool carefully, bool considerDependantHandles) => EnumerateReferencesWithFields(carefully, considerDependantHandles).Cast<IClrReference>();

        IComCallableWrapper? IClrValue.GetComCallableWrapper() => GetComCallableWrapper();

        IRuntimeCallableWrapper? IClrValue.GetRuntimeCallableWrapper() => GetRuntimeCallableWrapper();

        IClrValue IClrValue.ReadObjectField(string fieldName) => ReadObjectField(fieldName);

        IClrValue IClrValue.ReadValueTypeField(string fieldName) => ReadValueTypeField(fieldName);

        bool IClrValue.TryReadObjectField(string fieldName, [NotNullWhen(true)] out IClrValue? result)
        {
            bool res = TryReadObjectField(fieldName, out ClrObject obj);
            result = obj;
            return res;
        }

        bool IClrValue.TryReadValueTypeField(string fieldName, [NotNullWhen(true)] out IClrValue? result)
        {
            bool res = TryReadValueTypeField(fieldName, out ClrValueType val);
            result = val;
            return res;
        }
    }
}
