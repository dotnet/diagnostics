// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics
{
    public enum DbgShimCDacLoadPolicy : uint
    {
        PreferCDac = 0,
        CDacOnly = 1,
        LegacyDacOnly = 2,
    }

    public unsafe class ICLRDebuggingPolicy : CallableCOMWrapper
    {
        public static readonly Guid IID_ICLRDebuggingPolicy = new("2D3B4F6A-1C7E-4B2A-9E5D-7F1A6C0B8D34");

        private ref readonly ICLRDebuggingPolicyVTable VTable => ref Unsafe.AsRef<ICLRDebuggingPolicyVTable>(_vtable);

        public static ICLRDebuggingPolicy Create(IntPtr punk) => punk != IntPtr.Zero ? new ICLRDebuggingPolicy(punk) : null;

        private ICLRDebuggingPolicy(IntPtr punk)
            : base(IID_ICLRDebuggingPolicy, punk)
        {
            SuppressRelease();
        }

        public HResult SetCDacLoadPolicy(DbgShimCDacLoadPolicy policy)
        {
            return VTable.SetCDacLoadPolicy(Self, policy);
        }

        public HResult GetCDacLoadPolicy(out DbgShimCDacLoadPolicy policy)
        {
            return VTable.GetCDacLoadPolicy(Self, out policy);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct ICLRDebuggingPolicyVTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, DbgShimCDacLoadPolicy, int> SetCDacLoadPolicy;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, out DbgShimCDacLoadPolicy, int> GetCDacLoadPolicy;
        }
    }
}
