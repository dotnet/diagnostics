// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrInfo
    {
        ImmutableArray<byte> BuildId { get; }
        IDataTarget DataTarget { get; }
        ImmutableArray<DebugLibraryInfo> DebuggingLibraries { get; }
        ClrFlavor Flavor { get; }
        int IndexFileSize { get; }
        int IndexTimeStamp { get; }
        bool IsSingleFile { get; }
        ModuleInfo ModuleInfo { get; }
        Version Version { get; }

        IClrRuntime CreateRuntime();
        IClrRuntime CreateRuntime(DacLibrary dacLibrary);
        IClrRuntime CreateRuntime(string dacPath);
        IClrRuntime CreateRuntime(string dacPath, bool ignoreMismatch);
    }
}
