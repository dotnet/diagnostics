// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class Host : IHost
    {
        private readonly ServiceManager _serviceManager;
        private ServiceContainer _serviceContainer;
        private readonly List<ITarget> _targets = new();
        private int _targetIdFactory;

        public Host(HostType type)
        {
            HostType = type;

            _serviceManager = new ServiceManager();

            // Register all the services and commands in the Microsoft.Diagnostics.DebugServices.Implementation assembly
            _serviceManager.RegisterAssembly(typeof(Target).Assembly);
        }

        /// <summary>
        /// Service manager
        /// </summary>
        public ServiceManager ServiceManager => _serviceManager;

        /// <summary>
        /// Service container
        /// </summary>
        public ServiceContainer ServiceContainer => _serviceContainer ?? throw new InvalidOperationException();

        /// <summary>
        /// Creates and returns the global service container after finalizing all the service factories.
        /// </summary>
        public ServiceContainer CreateServiceContainer()
        {
            // Loading extensions or adding service factories not allowed after this point.
            _serviceManager.FinalizeServices();

            _serviceContainer = _serviceManager.CreateServiceContainer(ServiceScope.Global, parent: null);
            _serviceContainer.AddService<IServiceManager>(_serviceManager);
            _serviceContainer.AddService<IHost>(this);

            return _serviceContainer;
        }

        /// <summary>
        /// Cleans up all the targets created by this host.
        /// </summary>
        public void DestoryTargets()
        {
            foreach (ITarget target in _targets.ToArray())
            {
                target.Destroy();
            }
            _targets.Clear();
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType { get; }

        public IServiceProvider Services => _serviceContainer ?? throw new InvalidOperationException();

        public IEnumerable<ITarget> EnumerateTargets() => _targets.ToArray();

        public int AddTarget(ITarget target)
        {
            _targets.Add(target);
            target.OnDestroyEvent.Register(() => {
                _targets.Remove(target);
            });
            return _targetIdFactory++;
        }

        #endregion
    }
}
