// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime provider implementation
    /// </summary>
    [ProviderExport(Type = typeof(IRuntimeProvider))]
    public class RuntimeProvider : IRuntimeProvider
    {
        private readonly IServiceProvider _services;

        public RuntimeProvider(IServiceProvider services)
        {
            _services = services;
        }

        #region IRuntimeProvider

        /// <summary>
        /// Returns the list of .NET runtimes in the target
        /// </summary>
        /// <param name="startingRuntimeId">The starting runtime id for this provider</param>
        public IEnumerable<IRuntime> EnumerateRuntimes(int startingRuntimeId)
        {
            // The DataTarget isn't cached here because there is no way to flush it. It currently caches
            // ClrInfos, modules and PE images. It also isn't disposed because the DataTarget instance
            // needs to be around as long as the ClrInfos wrapped by the Runtime instances created below
            // which the life time is determined by the RuntimeService.
            DataTarget dataTarget = new(new CustomDataTarget(_services.GetService<IDataReader>()))
            {
                FileLocator = null
            };
            for (int i = 0; i < dataTarget.ClrVersions.Length; i++)
            {
                yield return new Runtime(_services, startingRuntimeId + i, dataTarget.ClrVersions[i]);
            }
        }

        #endregion
    }
}
