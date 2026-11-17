// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Information on a candidate runtime.
    /// </summary>
    public sealed class RuntimeDescription
    {
        public RuntimeDescription(IModule module, RuntimeType runtimeType, Version version)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            RuntimeType = runtimeType;
            Version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public IModule Module { get; }
        public RuntimeType RuntimeType { get; }
        public Version Version { get; }
        public bool IsSingleFile { get; set; }
        public int IndexFileSize { get; set; }
        public int IndexTimeStamp { get; set; }
        public ImmutableArray<byte> BuildId { get; set; } = ImmutableArray<byte>.Empty;
        public ulong ContractDescriptorAddress { get; set; }
        public ImmutableArray<RuntimeDebuggingLibrary> DebuggingLibraries { get; set; } = ImmutableArray<RuntimeDebuggingLibrary>.Empty;
    }
}
