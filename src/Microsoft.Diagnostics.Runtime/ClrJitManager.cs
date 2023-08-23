// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    public sealed class ClrJitManager : IClrJitManager
    {
        private readonly IClrNativeHeapHelpers _helpers;

        public ClrRuntime Runtime { get; }
        IClrRuntime IClrJitManager.Runtime => Runtime;

        public ulong Address { get; }

        public CodeHeapKind Kind { get; }

        internal ClrJitManager(ClrRuntime runtime, in JitManagerInfo info, IClrNativeHeapHelpers helpers)
        {
            Runtime = runtime;
            Address = info.Address;
            Kind = info.Kind;
            _helpers = helpers;
        }

        public IEnumerable<ClrNativeHeapInfo> EnumerateNativeHeaps() => _helpers.EnumerateNativeHeaps(this);
    }
}
