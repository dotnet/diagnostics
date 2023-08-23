// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrAppDomainData
    {
        public ClrAppDomain? SystemDomain { get; set; }
        public ClrAppDomain? SharedDomain { get; set; }
        public ImmutableArray<ClrAppDomain> AppDomains { get; set; }
        public Dictionary<ulong, ClrModule> Modules { get; } = new();
        public ClrModule? BaseClassLibrary { get; set; }

        internal ClrAppDomain? GetDomainByAddress(ulong address)
        {
            if (SystemDomain is not null && SystemDomain.Address == address)
                return SystemDomain;

            if (SharedDomain is not null && SharedDomain.Address == address)
                return SharedDomain;

            return AppDomains.FirstOrDefault(x => x.Address == address);
        }
    }
}
