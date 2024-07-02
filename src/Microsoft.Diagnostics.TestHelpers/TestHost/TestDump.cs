// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestDump : TestHost, IHost
    {
        private readonly ServiceManager _serviceManager;
        private readonly ServiceContainer _serviceContainer;
        private readonly ContextService _contextService;
        private readonly CommandService _commandService;
        private readonly SymbolService _symbolService;
        private DataTarget _dataTarget;
        private int _targetIdFactory;

        public TestDump(TestConfiguration config)
            : base(config)
        {
            _serviceManager = new ServiceManager();

            // Register all the services and commands in the Microsoft.Diagnostics.DebugServices.Implementation assembly
            _serviceManager.RegisterAssembly(typeof(Target).Assembly);

            // Loading extensions or adding service factories not allowed after this point.
            _serviceManager.FinalizeServices();

            _serviceContainer = _serviceManager.CreateServiceContainer(ServiceScope.Global, parent: null);
            _serviceContainer.AddService<IServiceManager>(_serviceManager);
            _serviceContainer.AddService<IHost>(this);

            _contextService = new(this);
            _serviceContainer.AddService<IContextService>(_contextService);

            _commandService = new();
            _serviceContainer.AddService<ICommandService>(_commandService);

            _symbolService = new(this);
            _serviceContainer.AddService<ISymbolService>(_symbolService);

            // Automatically enable symbol server support
            _symbolService.AddSymbolServer(timeoutInMinutes: 6, retryCount: 5);
            _symbolService.AddCachePath(_symbolService.DefaultSymbolCache);
        }

        public override void Dispose()
        {
            base.Dispose();
            _dataTarget?.Dispose();
            _dataTarget = null;
        }

        public ServiceManager ServiceManager => _serviceManager;

        public ServiceContainer ServiceContainer => _serviceContainer;

        public CommandService CommandService => _commandService;

        public override IReadOnlyList<string> ExecuteHostCommand(string commandLine) => _commandService.ExecuteAndCapture(commandLine, _contextService.Services);

        protected override ITarget GetTarget()
        {
            _dataTarget = DataTarget.LoadDump(DumpFile);

            OSPlatform targetPlatform = _dataTarget.DataReader.TargetPlatform;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                targetPlatform = OSPlatform.OSX;
            }
            _symbolService.AddDirectoryPath(Path.GetDirectoryName(DumpFile));
            return new TargetFromDataReader(_dataTarget.DataReader, targetPlatform, this, _targetIdFactory++, DumpFile);
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType => HostType.DotnetDump;

        public IServiceProvider Services => _serviceContainer;

        public IEnumerable<ITarget> EnumerateTargets() => Target != null ? new ITarget[] { Target } : Array.Empty<ITarget>();

        #endregion
    }
}
