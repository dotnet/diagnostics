// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ModuleData
    {
        public readonly ClrDataAddress Address;
        public readonly ClrDataAddress PEFile;
        public readonly ClrDataAddress ILBase;
        public readonly ClrDataAddress MetadataStart;
        public readonly ulong MetadataSize;
        public readonly ClrDataAddress Assembly;
        public readonly uint IsReflection;
        public readonly uint IsPEFile;
        public readonly ulong BaseClassIndex;
        public readonly ulong ModuleID;
        public readonly uint TransientFlags;
        public readonly ClrDataAddress TypeDefToMethodTableMap;
        public readonly ClrDataAddress TypeRefToMethodTableMap;
        public readonly ClrDataAddress MethodDefToDescMap;
        public readonly ClrDataAddress FieldDefToDescMap;
        public readonly ClrDataAddress MemberRefToDescMap;
        public readonly ClrDataAddress FileReferencesMap;
        public readonly ClrDataAddress ManifestModuleReferencesMap;
        public readonly ClrDataAddress LoaderAllocator;
        public readonly ClrDataAddress ThunkHeap;
        public readonly ulong ModuleIndex;
    }
}
