// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct FieldInfo
    {
        public readonly short NumInstanceFields;
        public readonly short NumStaticFields;
        public readonly short NumThreadStaticFields;
        public readonly ClrDataAddress FirstFieldAddress; // If non-null, you can retrieve more
        public readonly short ContextStaticOffset;
        public readonly short ContextStaticsSize;
    }
}
