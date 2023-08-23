// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal readonly struct MinidumpThreadEx
    {
        public readonly uint ThreadId;
        public readonly uint SuspendCount;
        public readonly uint PriorityClass;
        public readonly uint Priority;
        public readonly ulong Teb;
        public readonly MinidumpMemoryDescriptor Stack;
        public readonly MinidumpLocationDescriptor ThreadContext;
        public readonly MinidumpMemoryDescriptor BackingStore;
    }
}
