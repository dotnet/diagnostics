// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class ClrDataTask : CallableCOMWrapper
    {
        private static readonly Guid IID_IXCLRDataTask = new("A5B0BEEA-EC62-4618-8012-A24FFC23934C");

        public ClrDataTask(DacLibrary library, IntPtr pUnk)
            : base(library.OwningLibrary, IID_IXCLRDataTask, pUnk)
        {
        }

        private ref readonly ClrDataTaskVTable VTable => ref Unsafe.AsRef<ClrDataTaskVTable>(_vtable);

        public ClrStackWalk? CreateStackWalk(DacLibrary library, uint flags)
        {
            HResult hr = VTable.CreateStackWalk(Self, flags, out IntPtr pUnk);
            if (!hr)
            {
                Trace.TraceInformation($"CreateStackWalk failed: flags={flags:x}, hr={hr}");
                return null;
            }

            return new ClrStackWalk(library, pUnk);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct ClrDataTaskVTable
    {
        private readonly IntPtr GetProcess;
        private readonly IntPtr GetCurrentAppDomain;
        private readonly IntPtr GetUniqueID;
        private readonly IntPtr GetFlags;
        private readonly IntPtr IsSameObject;
        private readonly IntPtr GetManagedObject;
        private readonly IntPtr GetDesiredExecutionState;
        private readonly IntPtr SetDesiredExecutionState;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int> CreateStackWalk;
        private readonly IntPtr GetOSThreadID;
        private readonly IntPtr GetContext;
        private readonly IntPtr SetContext;
        private readonly IntPtr GetCurrentExceptionState;
        private readonly IntPtr Request;
        private readonly IntPtr GetName;
        private readonly IntPtr GetLastExceptionState;
    }
}