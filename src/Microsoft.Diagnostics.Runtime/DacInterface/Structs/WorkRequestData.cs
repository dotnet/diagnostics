// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct WorkRequestData
    {
        public readonly ClrDataAddress Function;
        public readonly ClrDataAddress Context;
        public readonly ClrDataAddress NextWorkRequest;
    }
}