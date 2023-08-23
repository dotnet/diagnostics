// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    /// <summary>
    /// <see cref="ClrAppDomain"/>
    /// </summary>
    public interface IClrAppDomain : IEquatable<IClrAppDomain>
    {
        /// <summary>
        /// <see cref="ClrAppDomain.Address"/>
        /// </summary>
        ulong Address { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.ApplicationBase"/>
        /// </summary>
        string? ApplicationBase { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.ConfigurationFile"/>
        /// </summary>
        string? ConfigurationFile { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.Id"/>
        /// </summary>
        int Id { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.LoaderAllocator"/>
        /// </summary>
        ulong LoaderAllocator { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.Modules"/>
        /// </summary>
        ImmutableArray<IClrModule> Modules { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.Name"/>
        /// </summary>
        string? Name { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.Runtime"/>
        /// </summary>
        IClrRuntime Runtime { get; }

        /// <summary>
        /// <see cref="ClrAppDomain.EnumerateLoaderAllocatorHeaps"/>
        /// </summary>
        IEnumerable<ClrNativeHeapInfo> EnumerateLoaderAllocatorHeaps();
    }
}
