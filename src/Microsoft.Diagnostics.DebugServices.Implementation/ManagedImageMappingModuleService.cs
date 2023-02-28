// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Module service implementation for managed image mapping
    /// </summary>
    public class ManagedImageMappingModuleService : ModuleService
    {
        private readonly IRuntimeService _runtimeService;

        public ManagedImageMappingModuleService(IServiceProvider services)
            : base(services)
        {
            _runtimeService = services.GetService<IRuntimeService>();
        }

        /// <summary>
        /// Get/create the modules dictionary.
        /// </summary>
        protected override Dictionary<ulong, IModule> GetModulesInner()
        {
            var modules = new Dictionary<ulong, IModule>();
            int moduleIndex = 0;

            IEnumerable<IRuntime> runtimes = _runtimeService.EnumerateRuntimes();
            if (runtimes.Any())
            {
                foreach (IRuntime runtime in runtimes)
                {
                    ClrRuntime clrRuntime = runtime.Services.GetService<ClrRuntime>();
                    if (clrRuntime is not null)
                    {
                        foreach (ClrModule clrModule in clrRuntime.EnumerateModules())
                        {
                            ModuleFromAddress module = new(this, moduleIndex, clrModule.ImageBase, clrModule.Size, clrModule.Name);
                            try
                            {
                                modules.Add(module.ImageBase, module);
                                moduleIndex++;
                            }
                            catch (ArgumentException)
                            {
                                Trace.TraceError($"GetModulesInner(): duplicate module base '{module}' dup '{modules[module.ImageBase]}'");
                            }
                        }
                    }
                }
            }

            return modules;
        }
    }
}
