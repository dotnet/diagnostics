// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    public sealed class ClrGenerationData : IClrGenerationData
    {
        public ulong StartSegment { get; }
        public ulong AllocationStart { get; }
        public ulong AllocationContextPointer { get; }
        public ulong AllocationContextLimit { get; }

        internal ClrGenerationData(in GenerationData generationData)
        {
            StartSegment = generationData.StartSegment;
            AllocationStart = generationData.AllocationStart;
            AllocationContextPointer = generationData.AllocationContextPointer;
            AllocationContextLimit = generationData.AllocationContextLimit;
        }
    }
}
