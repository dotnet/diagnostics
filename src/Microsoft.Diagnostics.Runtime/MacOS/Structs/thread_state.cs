// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct thread_state_t
    {
        [FieldOffset(0)]
        public x86_thread_state64_t x64;

        [FieldOffset(0)]
        public arm_thread_state64_t arm;
    }
}