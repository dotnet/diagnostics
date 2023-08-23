// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// The basic VTable for an IUnknown interface.
    /// </summary>
    public unsafe struct IUnknownVTable
    {
        public delegate* unmanaged[Stdcall]<IntPtr, in Guid, out IntPtr, int> QueryInterface;
        public delegate* unmanaged[Stdcall]<IntPtr, int> AddRef;
        public delegate* unmanaged[Stdcall]<IntPtr, int> Release;
    }
}