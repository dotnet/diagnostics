// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics
{    
    public unsafe class ICorDebug : CallableCOMWrapper
    {
        private static readonly Guid IID_ICorDebug = new Guid("3d6f5f61-7538-11d3-8d5b-00104b35e7ef");

        private ref readonly ICorDebugVTable VTable => ref Unsafe.AsRef<ICorDebugVTable>(_vtable);

        public static ICorDebug Create(IntPtr punk) => punk != IntPtr.Zero ? new ICorDebug(punk) : null;

        private ICorDebug(IntPtr punk)
            : base(new RefCountedFreeLibrary(IntPtr.Zero), IID_ICorDebug, punk)
        {
            SuppressRelease();
        }

        public HResult Initialize() => VTable.Initialize(Self);

        public HResult Terminate() => VTable.Terminate(Self);

        public HResult SetManagedHandler(IntPtr managedCallback) => VTable.SetManangedHandler(Self, managedCallback);

        public HResult DebugActiveProcess(int processId, out IntPtr process) => VTable.DebugActiveProcess(Self, processId, 0, out process);

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct ICorDebugVTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int> Initialize;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int> Terminate;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> SetManangedHandler;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int> SetUnmanangedHandler;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int> CreateProcess;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, int, out IntPtr, int> DebugActiveProcess;
        }
    }
}
