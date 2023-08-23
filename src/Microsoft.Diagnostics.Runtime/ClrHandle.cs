// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a CLR handle in the target process.
    /// </summary>
    public class ClrHandle : ClrRoot, IClrHandle
    {
        internal ClrHandle(ClrAppDomain parent, ulong address, ClrObject obj, ClrHandleKind kind, uint referenceCount = 0)
            : base(address, obj, ClrRootKind.None, false, kind is ClrHandleKind.AsyncPinned or ClrHandleKind.Pinned)
        {
            AppDomain = parent;
            HandleKind = kind;
            ReferenceCount = referenceCount;
            RootKind = IsStrong ? (ClrRootKind)HandleKind : ClrRootKind.None;
        }
        internal ClrHandle(ClrAppDomain parent, ulong address, ClrObject obj, ClrHandleKind kind, ClrObject dependent)
            : base(address, obj, ClrRootKind.None, false, kind is ClrHandleKind.AsyncPinned or ClrHandleKind.Pinned)
        {
            AppDomain = parent;
            HandleKind = kind;
            RootKind = IsStrong ? (ClrRootKind)HandleKind : ClrRootKind.None;
            Dependent = dependent;
        }

        /// <summary>
        /// Gets the type of handle.
        /// </summary>
        public ClrHandleKind HandleKind { get; }

        /// <summary>
        /// If this handle is a RefCount handle, this returns the reference count.
        /// RefCount handles with a RefCount > 0 are strong.
        /// </summary>
        public virtual uint ReferenceCount { get; }

        /// <summary>
        /// Gets the dependent handle target if this is a dependent handle.
        /// </summary>
        public virtual ClrObject Dependent { get; }

        /// <summary>
        /// Gets the AppDomain the handle resides in.
        /// </summary>
        public ClrAppDomain AppDomain { get; }

        /// <summary>
        /// Gets a value indicating whether the handle is strong (roots the object).
        /// </summary>
        public bool IsStrong => HandleKind switch
        {
            ClrHandleKind.RefCounted => ReferenceCount > 0,
            ClrHandleKind.WeakLong or
            ClrHandleKind.WeakShort or
            ClrHandleKind.Dependent or
            ClrHandleKind.WeakWinRT => false,
            _ => true,
        };

        IClrAppDomain IClrHandle.AppDomain => AppDomain;

        IClrValue IClrHandle.Dependent => Dependent;

        /// <summary>
        /// ToString override.
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{HandleKind.GetName()} @{Address:x12} -> {Object}";
    }
}