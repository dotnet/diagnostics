// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Runtime service implementation
    /// </summary>
    [ServiceExport(Type = typeof(IRuntimeService), Scope = ServiceScope.Target)]
    public class RuntimeService : IRuntimeService, IDisposable
    {
        private readonly IServiceProvider _services;
        private readonly IServiceManager _serviceManager;
        private List<IRuntime> _runtimes;

        public RuntimeService(IServiceProvider services, ITarget target)
        {
            _services = services;
            _serviceManager = services.GetService<IServiceManager>();
            target.OnFlushEvent.Register(Flush);
        }

        void IDisposable.Dispose() => Flush();

        private void Flush()
        {
            if (_runtimes is not null)
            {
                foreach (IRuntime runtime in _runtimes)
                {
                    if (runtime is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _runtimes.Clear();
                _runtimes = null;
            }
        }

        #region IRuntimeService

        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        public IEnumerable<IRuntime> EnumerateRuntimes()
        {
            if (_runtimes is null)
            {
                _runtimes = new List<IRuntime>();
                foreach (ServiceFactory factory in _serviceManager.EnumerateProviderFactories(typeof(IRuntimeProvider)))
                {
                    IRuntimeProvider provider = (IRuntimeProvider)factory(_services);
                    _runtimes.AddRange(provider.EnumerateRuntimes(_runtimes.Count));
                }
            }
            return _runtimes;
        }

        #endregion
    }
}
