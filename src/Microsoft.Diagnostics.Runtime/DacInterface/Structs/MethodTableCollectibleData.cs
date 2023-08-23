// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal readonly struct MethodTableCollectibleData
    {
        public readonly ClrDataAddress LoaderAllocatorObjectHandle;
        public readonly uint Collectible;
    }
}