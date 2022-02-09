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
    public unsafe class ICorDebugController : CallableCOMWrapper
    {
        private static readonly Guid IID_ICorDebugController = new Guid("3D6F5F62-7538-11D3-8D5B-00104B35E7EF");

        private ref readonly ICorDebugControllerVTable VTable => ref Unsafe.AsRef<ICorDebugControllerVTable>(_vtable);

        public static ICorDebugController Create(IntPtr punk) => punk != IntPtr.Zero ? new ICorDebugController(punk) : null;

        private ICorDebugController(IntPtr punk)
            : base(new RefCountedFreeLibrary(IntPtr.Zero), IID_ICorDebugController, punk)
        {
            SuppressRelease();
        }

        public HResult Stop() => VTable.Stop(Self, uint.MaxValue);

        public HResult Continue(bool isOutOfBand) => VTable.Continue(Self, isOutOfBand ? 1 : 0);

        public HResult Detach() => VTable.Detach(Self);

        public HResult Terminate(uint exitCode) => VTable.Terminate(Self, exitCode);

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct ICorDebugControllerVTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, HResult> Stop;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, HResult> Continue;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, HResult> IsRunning_dummy;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, HResult> HasQueuedCallbacks_dummy;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, HResult> EnumerateThreads_dummy;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, HResult> SetAllThreadsDebugState_dummy;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, HResult> Detach;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, HResult> Terminate;
        }
    }
}
