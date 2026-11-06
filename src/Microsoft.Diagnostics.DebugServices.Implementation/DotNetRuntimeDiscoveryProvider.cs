// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    [ProviderExport(Type = typeof(IRuntimeDescriptionProvider))]
    public sealed class DotNetRuntimeDiscoveryProvider : IRuntimeDescriptionProvider
    {
        private readonly RuntimeDescriptionBuilder _builder;

        public DotNetRuntimeDiscoveryProvider(IServiceProvider services)
        {
            _builder = new RuntimeDescriptionBuilder(services);
        }

        public int Priority => 0;

        public bool TryDescribeRuntime(IModule module, out RuntimeDescription description)
        {
            description = TryGetRuntimeFlavor(module, out RuntimeType flavor)
                ? _builder.Create(module, runtimeInfo: 0, flavor)
                : null;
            return description is not null;
        }

        private static bool TryGetRuntimeFlavor(IModule module, out RuntimeType flavor)
        {
            string moduleName = PathUtilities.GetFileName(module.FileName);
            if (string.Equals(moduleName, "clr.dll", StringComparison.OrdinalIgnoreCase))
            {
                flavor = RuntimeType.Desktop;
                return true;
            }
            flavor = RuntimeType.NetCore;
            return string.Equals(moduleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase)
                || string.Equals(moduleName, "libcoreclr.dylib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(moduleName, "libcoreclr.so", StringComparison.Ordinal);
        }
    }
}
