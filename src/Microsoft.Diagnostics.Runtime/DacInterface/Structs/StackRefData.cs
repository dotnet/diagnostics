// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct StackRefData
    {
        public readonly uint HasRegisterInformation;
        public readonly int Register;
        public readonly int Offset;
        public readonly ClrDataAddress Address;
        public readonly ClrDataAddress Object;
        public readonly uint Flags;

        public readonly uint SourceType;
        public readonly ClrDataAddress Source;
        public readonly ClrDataAddress StackPointer;
    }
}
