// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IDataTarget : IDisposable
    {
        CacheOptions CacheOptions { get; }
        ImmutableArray<IClrInfo> ClrVersions { get; }
        IDataReader DataReader { get; }
        IFileLocator? FileLocator { get; set; }

        IEnumerable<ModuleInfo> EnumerateModules();
        void SetSymbolPath(string symbolPath);
    }
}