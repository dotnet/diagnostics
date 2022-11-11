// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime provider implementation
    /// </summary>
    [ServiceExport(Type = typeof(IRuntimeProvider), Scope = ServiceScope.Target)]
    public class RuntimeProvider : IRuntimeProvider, IDisposable
    {
        private readonly IServiceProvider _services;
        private DataTarget _dataTarget;

        public RuntimeProvider(IServiceProvider services, ITarget target)
        {
            _services = services;
            target.OnFlushEvent.Register(Flush);
        }

        void IDisposable.Dispose() => Flush();

        private void Flush()
        {
            _dataTarget?.Dispose();
            _dataTarget = null;
        }

        #region IRuntimeProvider

        /// <summary>
        /// Returns the list of .NET runtimes in the target
        /// </summary>
        /// <param name="startingRuntimeId">The starting runtime id for this provider</param>
        public IEnumerable<IRuntime> EnumerateRuntimes(int startingRuntimeId)
        {
            if (_dataTarget is null)
            {
                _dataTarget = new DataTarget(new CustomDataTarget(_services.GetService<IDataReader>())) {
                    FileLocator = null
                };
            }
            for (int i = 0; i < _dataTarget.ClrVersions.Length; i++)
            {
                yield return new Runtime(_services, startingRuntimeId + i, _dataTarget.ClrVersions[i]);
            }
        }

        #endregion
    }
}
