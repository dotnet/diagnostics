// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    [ProviderExport(Type = typeof(IRuntimeDescriptionProvider))]
    public sealed class SingleFileRuntimeDiscoveryProvider : IRuntimeDescriptionProvider
    {
        private readonly RuntimeDescriptionBuilder _builder;

        public SingleFileRuntimeDiscoveryProvider(IServiceProvider services)
        {
            _builder = new RuntimeDescriptionBuilder(services);
        }

        public int Priority => -1;

        public bool TryDescribeRuntime(IModule module, out RuntimeDescription description)
        {
            description = null;
            if (module.Target.OperatingSystem == OSPlatform.Windows &&
                !string.Equals(Path.GetExtension(module.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ulong runtimeInfo = RuntimeDescriptionBuilder.GetExportAddress(module, RuntimeInfo.RUNTIME_INFO_SYMBOL);
            if (runtimeInfo == 0)
            {
                return false;
            }
            description = _builder.Create(module, runtimeInfo, RuntimeType.NetCore);
            if (description is null)
            {
                return false;
            }
            description.IsSingleFile = true;
            return true;
        }
    }
}
