// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    [StructLayout(LayoutKind.Explicit)]
    public struct VectorRegisterArea
    {
        public const int VectorRegisterSize = 26;

        [FieldOffset(0x0)]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VectorRegisterSize)]
        public M128A[]? VectorRegister;

        [FieldOffset(0x1a0)]
        public ulong VectorControl;

        public VectorRegisterArea(VectorRegisterArea other) : this()
        {
            if (other.VectorRegister is null)
                throw new ArgumentException("Cannot have empty VectorRegister.", nameof(other));

            VectorRegister ??= new M128A[other.VectorRegister.Length];

            for (int i = 0; i < VectorRegisterSize; ++i)
                VectorRegister[i] = other.VectorRegister[i];

            VectorControl = other.VectorControl;
        }

        public void Clear()
        {
            if (VectorRegister != null)
                for (int i = 0; i < VectorRegisterSize; ++i)
                    VectorRegister[i].Clear();

            VectorControl = 0;
        }
    }
}
