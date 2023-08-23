// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    /// <summary>
    /// <see cref="ClrModule"/>
    /// </summary>
    public interface IClrModule : IEquatable<IClrModule>
    {
        /// <summary>
        /// <see cref="ClrModule.Address"/>
        /// </summary>
        ulong Address { get; }

        /// <summary>
        /// <see cref="ClrModule.AppDomain"/>
        /// </summary>
        IClrAppDomain AppDomain { get; }

        /// <summary>
        /// <see cref="ClrModule.AssemblyAddress"/>
        /// </summary>
        ulong AssemblyAddress { get; }


        /// <summary>
        /// <see cref="ClrModule.AssemblyName"/>
        /// </summary>
        string? AssemblyName { get; }

        /// <summary>
        /// <see cref="ClrModule.DebuggingMode"/>
        /// </summary>
        DebuggableAttribute.DebuggingModes DebuggingMode { get; }

        /// <summary>
        /// <see cref="ClrModule.ImageBase"/>
        /// </summary>
        ulong ImageBase { get; }

        /// <summary>
        /// <see cref="ClrModule.IsDynamic"/>
        /// </summary>
        bool IsDynamic { get; }

        /// <summary>
        /// <see cref="ClrModule.IsPEFile"/>
        /// </summary>
        bool IsPEFile { get; }

        /// <summary>
        /// <see cref="ClrModule.Layout"/>
        /// </summary>
        ModuleLayout Layout { get; }


        /// <summary>
        /// <see cref="ClrModule.LoaderAllocator"/>
        /// </summary>
        ulong LoaderAllocator { get; }

        /// <summary>
        /// <see cref="ClrModule.MetadataAddress"/>
        /// </summary>
        ulong MetadataAddress { get; }

        /// <summary>
        /// <see cref="ClrModule.MetadataLength"/>
        /// </summary>
        ulong MetadataLength { get; }

        /// <summary>
        /// <see cref="ClrModule.Name"/>
        /// </summary>
        string? Name { get; }

        /// <summary>
        /// <see cref="ClrModule.Pdb"/>
        /// </summary>
        PdbInfo? Pdb { get; }

        /// <summary>
        /// <see cref="ClrModule.Size"/>
        /// </summary>
        ulong Size { get; }

        /// <summary>
        /// <see cref="ClrModule.ThunkHeap"/>
        /// </summary>
        ulong ThunkHeap { get; }


        /// <summary>
        /// <see cref="ClrModule.EnumerateLoaderAllocatorHeaps"/>
        /// </summary>
        IEnumerable<ClrNativeHeapInfo> EnumerateLoaderAllocatorHeaps();

        /// <summary>
        /// <see cref="ClrModule.EnumerateThunkHeap"/>
        /// </summary>
        IEnumerable<ClrNativeHeapInfo> EnumerateThunkHeap();

        /// <summary>
        /// <see cref="ClrModule.EnumerateTypeDefToMethodTableMap"/>
        /// </summary>
        IEnumerable<(ulong MethodTable, int Token)> EnumerateTypeDefToMethodTableMap();

        /// <summary>
        /// <see cref="ClrModule.EnumerateTypeRefToMethodTableMap"/>
        /// </summary>
        IEnumerable<(ulong MethodTable, int Token)> EnumerateTypeRefToMethodTableMap();


        /// <summary>
        /// <see cref="ClrModule.GetTypeByName(string)"/>
        /// </summary>
        IClrType? GetTypeByName(string name);
    }
}