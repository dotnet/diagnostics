// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct DacOOMData
    {
        public readonly OutOfMemoryReason Reason;
        public readonly ulong AllocSize;
        public readonly ulong AvailablePageFileMB;
        public readonly ulong GCIndex;
        public readonly GetMemoryFailureReason GetMemoryFailure;
        public readonly ulong Size;
        public readonly int IsLOH;
    }
}