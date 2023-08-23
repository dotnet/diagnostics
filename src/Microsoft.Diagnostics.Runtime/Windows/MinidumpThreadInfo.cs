// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal readonly struct MinidumpThreadInfo
    {
        public readonly uint ThreadId;
        public readonly uint DumpFlags;
        public readonly uint DumpError;
        public readonly uint ExitStatus;
        public readonly ulong CreateTime;
        public readonly ulong ExitTime;
        public readonly ulong KernelTime;
        public readonly ulong UserTime;
        public readonly ulong StartAddress;
        public readonly ulong Affinity;
    }
}
