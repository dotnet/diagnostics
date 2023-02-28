// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting;

namespace Microsoft.Diagnostics
{    
    public unsafe class ICLRDebugging : CallableCOMWrapper
    {
        public static readonly Guid IID_ICLRDebugging = new Guid("D28F3C5A-9634-4206-A509-477552EEFB10");
        public static readonly Guid CLSID_ICLRDebugging = new Guid("BACC578D-FBDD-48A4-969F-02D932B74634");

        private ref readonly ICLRDebuggingVTable VTable => ref Unsafe.AsRef<ICLRDebuggingVTable>(_vtable);

        public static ICLRDebugging Create(IntPtr punk) => punk != IntPtr.Zero ? new ICLRDebugging(punk) : null;

        private ICLRDebugging(IntPtr punk)
            : base(new RefCountedFreeLibrary(IntPtr.Zero), IID_ICLRDebugging, punk)
        {
            SuppressRelease();
        }

        public HResult OpenVirtualProcess(
            ulong moduleBaseAddress,
            IntPtr dataTarget,
            IntPtr libraryProvider,
            ClrDebuggingVersion maxDebuggerSupportedVersion,
            in Guid riidProcess,
            out IntPtr process,
            out ClrDebuggingVersion version, 
            out ClrDebuggingProcessFlags flags)
        {
            return VTable.OpenVirtualProcess(
                Self,
                moduleBaseAddress,
                dataTarget,
                libraryProvider,
                in maxDebuggerSupportedVersion,
                in riidProcess,
                out process,
                out version,
                out flags);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct ICLRDebuggingVTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, IntPtr, in ClrDebuggingVersion, in Guid, out IntPtr, out ClrDebuggingVersion, out ClrDebuggingProcessFlags, int> OpenVirtualProcess;
        }
    }
}
