// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// Base class for COM related objects in ClrMD.
    /// </summary>
    public abstract class COMHelper
    {
        protected static readonly Guid IUnknownGuid = new("00000000-0000-0000-C000-000000000046");

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        protected delegate int AddRefDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        protected delegate int ReleaseDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        protected delegate int QueryInterfaceDelegate(IntPtr self, in Guid guid, out IntPtr ptr);

        /// <summary>
        /// Release an IUnknown pointer.
        /// </summary>
        /// <param name="pUnk">A pointer to the IUnknown interface to release.</param>
        /// <returns>The result of pUnk->Release().</returns>
        public static unsafe int Release(IntPtr pUnk)
        {
            if (pUnk == IntPtr.Zero)
                return 0;

            IUnknownVTable* vtable = *(IUnknownVTable**)pUnk;

            return vtable->Release(pUnk);
        }

        public static unsafe HResult QueryInterface(IntPtr pUnk, in Guid riid, out IntPtr result)
        {
            result = IntPtr.Zero;

            if (pUnk == IntPtr.Zero)
                return HResult.E_INVALIDARG;

            IUnknownVTable* vtable = *(IUnknownVTable**)pUnk;

            return (HResult)vtable->QueryInterface(pUnk, riid, out result);
        }
    }
}