// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents an array in the target process.
    /// </summary>
    public struct ClrArray : IClrArray
    {
        /// <summary>
        /// Gets the address of the object.
        /// </summary>
        public ulong Address { get; }

        /// <summary>
        /// Gets the type of the object.
        /// </summary>
        public ClrType Type { get; }

        private int _length;

        /// <summary>
        /// Gets the count of elements in this array.
        /// </summary>
        public int Length
        {
            get
            {
                if (_length == -1)
                {
                    _length = Type.Helpers.DataReader.Read<int>(Address + (ulong)IntPtr.Size);
                }

                return _length;
            }
        }

        public readonly int Rank
        {
            get
            {
                int rank = MultiDimensionalRank;
                return rank != 0 ? rank : 1;
            }
        }

        private readonly bool IsMultiDimensional => Type.StaticSize > (uint)(3 * IntPtr.Size);

        private readonly int MultiDimensionalRank => (int)((Type.StaticSize - (uint)(3 * IntPtr.Size)) / (2 * sizeof(int)));

        IClrType IClrArray.Type => Type;

        internal ClrArray(ulong address, ClrType type)
        {
            Address = address;
            Type = type;
            // default uninitialized value for size. Will be lazy loaded.
            _length = -1;
        }

        /// <summary>
        /// Gets <paramref name="count"/> element values from the array.
        /// </summary>
        public T[]? ReadValues<T>(int start, int count) where T : unmanaged
        {
            if (start < 0 || start >= Length)
                throw new ArgumentOutOfRangeException(nameof(start));

            if (count < 0 || start + count > Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            return Type.ReadArrayElements<T>(Address, start, count);
        }

        /// <summary>
        /// Determines whether this instance and another specific <see cref="ClrArray"/> have the same value.
        /// <para>Instances are considered equal when they have the same <see cref="Address"/>.</para>
        /// </summary>
        /// <param name="obj">The <see cref="ClrArray"/> to compare to this instance.</param>
        /// <returns><see langword="true"/> if the <see cref="Address"/> of the parameter is the same as <see cref="Address"/> in this instance; <see langword="false"/> otherwise.</returns>
        public override readonly bool Equals(object? obj) => obj switch
        {
            null => false,
            ulong address => Address == address,
            ClrArray clrArray => Address == clrArray.Address,
            ClrObject clrObject => Address == clrObject.Address,
            _ => false
        };

        public bool Equals(ClrArray other) => Address == other.Address;

        /// <summary>
        /// Returns the hash code for this <see cref="ClrArray"/>.
        /// </summary>
        /// <returns>An <see cref="int"/> hash code for this instance.</returns>
        public override readonly int GetHashCode() => Address.GetHashCode();

        public int GetLength(int dimension)
        {
            int rank = MultiDimensionalRank;
            if (rank == 0 && dimension == 0)
                return Length;

            if ((uint)dimension >= rank)
                throw new ArgumentOutOfRangeException(nameof(dimension));

            return GetMultiDimensionalBound(dimension);
        }

        public readonly int GetLowerBound(int dimension)
        {
            int rank = MultiDimensionalRank;
            if (rank == 0 && dimension == 0)
                return 0;

            if ((uint)dimension >= rank)
                throw new ArgumentOutOfRangeException(nameof(dimension));

            return GetMultiDimensionalBound(rank + dimension);
        }

        public int GetUpperBound(int dimension)
        {
            int rank = MultiDimensionalRank;
            if (rank == 0 && dimension == 0)
                return Length - 1;

            if ((uint)dimension >= rank)
                throw new ArgumentOutOfRangeException(nameof(dimension));

            int length = GetMultiDimensionalBound(dimension);
            int lowerBound = GetMultiDimensionalBound(rank + dimension);
            return length + lowerBound - 1;
        }

        public unsafe T GetValue<T>(int index) where T : unmanaged
        {
            if (sizeof(T) != Type.ComponentSize)
                throw new ArgumentException($"{typeof(T).Name} is 0x{sizeof(T):x} bytes but the array element is 0x{Type.ComponentSize:x}.");

            return ReadValue<T>(index);
        }

        public unsafe T GetValue<T>(params int[] indices) where T : unmanaged
        {
            if (sizeof(T) != Type.ComponentSize)
                throw new ArgumentException($"{typeof(T).Name} is 0x{sizeof(T):x} bytes but the array element is 0x{Type.ComponentSize:x}.");

            return ReadValue<T>(indices);
        }

        public ClrValueType GetStructValue(int index)
        {
            if (Type.ComponentType is null)
                return default;

            if (Type.ComponentType.IsObjectReference)
                throw new InvalidOperationException($"{Type} does not contain value type instances.");

            ulong address = GetElementAddress(Type.ComponentSize, index);
            return new ClrValueType(address, Type.ComponentType, interior: true);
        }

        public ClrValueType GetStructValue(params int[] indices)
        {
            if (Type.ComponentType is null)
                return default;

            if (Type.ComponentType.IsObjectReference)
                throw new InvalidOperationException($"{Type} does not contain value type instances.");

            ulong address = GetElementAddress(Type.ComponentSize, indices);
            return new ClrValueType(address, Type.ComponentType, interior: true);
        }

        public ClrObject GetObjectValue(int index)
        {
            if (Type.ComponentType != null && !Type.ComponentType.IsObjectReference)
                throw new InvalidOperationException($"{Type} does not contain object references.");

            return Type.Heap.GetObject(ReadValue<nuint>(index));
        }

        public ClrObject GetObjectValue(params int[] indices)
        {
            if (Type.ComponentType != null && !Type.ComponentType.IsObjectReference)
                throw new InvalidOperationException($"{Type} does not contain object references.");

            return Type.Heap.GetObject(ReadValue<nuint>(indices));
        }

        private unsafe T ReadValue<T>(int index) where T : unmanaged
        {
            return Type.Helpers.DataReader.Read<T>(GetElementAddress(sizeof(T), index));
        }

        private unsafe T ReadValue<T>(int[] indices) where T : unmanaged
        {
            return Type.Helpers.DataReader.Read<T>(GetElementAddress(sizeof(T), indices));
        }

        private unsafe ulong GetElementAddress(int elementSize, int index)
        {
            if (Rank != 1)
                throw new ArgumentException($"Array {Address:x} was not a one-dimensional array. Type: {Type?.Name ?? "null"}");

            int valueOffset = index;
            int dataByteOffset = 2 * sizeof(nint);

            if (IsMultiDimensional)
            {
                valueOffset -= GetMultiDimensionalBound(1);
                if (unchecked((uint)valueOffset) >= GetMultiDimensionalBound(0))
                    throw new ArgumentOutOfRangeException(nameof(index));

                dataByteOffset += 2 * sizeof(int);
            }
            else
            {
                if (unchecked((uint)valueOffset) >= Length)
                    throw new ArgumentOutOfRangeException(nameof(index));
            }

            int valueByteOffset = dataByteOffset + valueOffset * elementSize;
            return Address + (ulong)valueByteOffset;
        }

        private unsafe ulong GetElementAddress(int elementSize, int[] indices)
        {
            if (indices is null)
                throw new ArgumentNullException(nameof(indices));

            int rank = Rank;
            if (rank != indices.Length)
                throw new ArgumentException($"Indices length does not match the array rank. Array {Address:x} Rank = {rank}, {nameof(indices)} Rank = {indices.Length}");

            int valueOffset = 0;
            int dataByteOffset = 2 * sizeof(nint);

            if (rank == 1)
            {
                if (IsMultiDimensional)
                {
                    valueOffset = indices[0] - GetMultiDimensionalBound(1);
                    if ((uint)valueOffset >= GetMultiDimensionalBound(0))
                        throw new ArgumentOutOfRangeException(nameof(indices));

                    dataByteOffset += 2 * sizeof(int);
                }
                else
                {
                    valueOffset = indices[0];
                    if ((uint)valueOffset >= Length)
                        throw new ArgumentOutOfRangeException(nameof(indices));
                }
            }
            else
            {
                for (int dimension = 0; dimension < rank; dimension++)
                {
                    int currentValueOffset = indices[dimension] - GetMultiDimensionalBound(rank + dimension);
                    if ((uint)currentValueOffset >= GetMultiDimensionalBound(dimension))
                        throw new ArgumentOutOfRangeException(nameof(indices));

                    valueOffset *= GetMultiDimensionalBound(dimension);
                    valueOffset += currentValueOffset;
                }

                dataByteOffset += 2 * sizeof(int) * rank;
            }

            int valueByteOffset = dataByteOffset + valueOffset * elementSize;
            return Address + (ulong)valueByteOffset;
        }

        // |<-------------------------- Type.StaticSize -------------------------->|
        // |                                                                       |
        // [    nint    ||       nint       ||  nint  || int[rank] |   int[rank]   ||          ]
        // [ sync block || Type.MethodTable || Length || GetLength | GetLowerBound || elements ]
        //                 ^
        //                 | Address
        private readonly int GetMultiDimensionalBound(int offset) =>
            Type.Helpers.DataReader.Read<int>(Address + (ulong)(2 * IntPtr.Size) + (ulong)(offset * sizeof(int)));

        IClrValue IClrArray.GetObjectValue(int index) => GetObjectValue(index);

        IClrValue IClrArray.GetObjectValue(params int[] indices) => GetObjectValue(indices);

        IClrValue IClrArray.GetStructValue(int index) => GetStructValue(index);

        IClrValue IClrArray.GetStructValue(params int[] indices) => GetStructValue(indices);
    }
}