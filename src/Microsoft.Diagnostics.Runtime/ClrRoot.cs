// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    public class ClrRoot : IClrRoot, IEquatable<ClrRoot>
    {
        public bool Equals(IClrRoot? other)
        {
            return other is not null && Address == other.Address && RootKind == other.RootKind && Object.Equals(other.Object);
        }
        public bool Equals(ClrRoot? other)
        {
            return other is not null && Address == other.Address && RootKind == other.RootKind && Object.Equals(other.Object);
        }

        public override bool Equals(object? obj)
        {
            if (obj is IClrRoot other)
                return other.Equals(this);

            return false;
        }

        public override int GetHashCode() => Address.GetHashCode() ^ RootKind.GetHashCode() ^ Object.GetHashCode();

        /// <summary>
        /// Gets the address in memory of the root.  Typically dereferencing this address will
        /// give you the associated Object, but not always.
        /// </summary>
        public ulong Address { get; }

        /// <summary>
        /// Gets the object the root points to.
        /// </summary>
        public virtual ClrObject Object { get; }

        IClrValue IClrRoot.Object => Object;

        /// <summary>
        /// Gets the kind of root this is.
        /// </summary>
        public ClrRootKind RootKind { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether Address may point to the interior of an object (i.e. not the start of an object).
        /// If Address happens to point to the start of the object, ClrRoot.Object will be filled
        /// as normal, otherwise ClrRoot.Object.IsNull will be <see langword="true"/>.  In order to properly account
        /// for interior objects, you must read the value out of Address then find the object which
        /// contains it.
        /// </summary>
        public bool IsInterior { get; }

        /// <summary>
        /// Gets a value indicating whether the object is pinned in place by this root and will not be relocated by the GC.
        /// </summary>
        public bool IsPinned { get; }

        IClrStackFrame? IClrRoot.StackFrame => null;

        string? IClrRoot.RegisterName => null;

        int IClrRoot.RegisterOffset => 0;

        public ClrRoot(ulong address, ClrObject obj, ClrRootKind rootKind, bool isInterior, bool isPinned)
        {
            Address = address;
            Object = obj;
            RootKind = rootKind;
            IsInterior = isInterior;
            IsPinned = isPinned;
        }
    }
}
