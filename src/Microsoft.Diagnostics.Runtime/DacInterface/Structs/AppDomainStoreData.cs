// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct AppDomainStoreData
    {
        public readonly ClrDataAddress SharedDomain;
        public readonly ClrDataAddress SystemDomain;
        public readonly int AppDomainCount;
    }
}
