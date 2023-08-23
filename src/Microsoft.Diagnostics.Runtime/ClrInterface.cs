// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// An interface implementation in the target process.
    /// </summary>
    public sealed class ClrInterface :
#nullable disable // to enable use with both T and T? for reference types due to IEquatable<T> being invariant
        IEquatable<ClrInterface>
#nullable restore
    {
        /// <summary>
        /// Gets the typename of the interface.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the interface that this interface inherits from.
        /// </summary>
        public ClrInterface? BaseInterface { get; }

        /// <summary>
        /// Display string for this interface.
        /// </summary>
        /// <returns>Display string for this interface.</returns>
        public override string ToString() => Name;

        public ClrInterface(string name, ClrInterface? baseInterface)
        {
            Name = name;
            BaseInterface = baseInterface;
        }

        /// <summary>
        /// Equals override.
        /// </summary>
        /// <param name="obj">Object to compare to.</param>
        /// <returns>True if this interface equals another.</returns>
        public override bool Equals(object? obj) => Equals(obj as ClrInterface);

        public bool Equals(ClrInterface? other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (other is null)
                return false;

            return Name == other.Name && BaseInterface == other.BaseInterface;
        }

        /// <summary>
        /// GetHashCode override.
        /// </summary>
        /// <returns>A hashcode for this object.</returns>
        public override int GetHashCode()
        {
            int hashCode = 0;

            if (Name != null)
                hashCode ^= Name.GetHashCode();

            if (BaseInterface != null)
                hashCode ^= BaseInterface.GetHashCode();

            return hashCode;
        }

        public static bool operator ==(ClrInterface? left, ClrInterface? right)
        {
            if (right is null)
                return left is null;

            return right.Equals(left);
        }

        public static bool operator !=(ClrInterface? left, ClrInterface? right) => !(left == right);
    }
}