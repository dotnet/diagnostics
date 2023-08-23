// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    public class ClrOutOfMemoryInfo : IClrOutOfMemoryInfo
    {
        internal ClrOutOfMemoryInfo(in DacOOMData oomData)
        {
            Reason = oomData.Reason;
            GetMemoryFailure = oomData.GetMemoryFailure;
            IsLargeObjectHeap = oomData.IsLOH != 0;
            AllocSize = oomData.AllocSize;
            AvailablePageFileMB = oomData.AvailablePageFileMB;
            GCIndex = oomData.GCIndex;
            Size = oomData.Size;
        }

        public OutOfMemoryReason Reason { get; }
        public GetMemoryFailureReason GetMemoryFailure { get; }
        public bool IsLargeObjectHeap { get; }
        public ulong AllocSize { get; }
        public ulong AvailablePageFileMB { get; }
        public ulong GCIndex { get; }
        public ulong Size { get; }
    }
}
