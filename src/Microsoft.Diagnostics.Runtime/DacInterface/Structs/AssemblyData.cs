// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct AssemblyData
    {
        public readonly ClrDataAddress Address;
        public readonly ClrDataAddress ClassLoader;
        public readonly ClrDataAddress ParentDomain;
        public readonly ClrDataAddress AppDomain;
        public readonly ClrDataAddress AssemblySecurityDescriptor;
        public readonly int Dynamic;
        public readonly int ModuleCount;
        public readonly uint LoadContext;
        public readonly int IsDomainNeutral;
        public readonly uint LocationFlags;
    }
}
