// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Reflection;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrMethod
    {
        MethodAttributes Attributes { get; }
        MethodCompilationType CompilationType { get; }
        HotColdRegions HotColdInfo { get; }
        ImmutableArray<ILToNativeMap> ILOffsetMap { get; }
        bool IsClassConstructor { get; }
        bool IsConstructor { get; }
        int MetadataToken { get; }
        ulong MethodDesc { get; }
        string? Name { get; }
        ulong NativeCode { get; }
        string? Signature { get; }
        IClrType Type { get; }

        ILInfo? GetILInfo();
        int GetILOffset(ulong addr);
    }
}