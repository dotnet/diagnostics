using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestDump : TestHost, IHost
    {
        private readonly ServiceManager _serviceManager;
        private readonly IServiceContainer _serviceContainer;
        private readonly ContextService _contextService;
        private readonly SymbolService _symbolService;
        private DataTarget _dataTarget;
        private int _targetIdFactory;

        public TestDump(TestConfiguration config)
            : base(config)
        {
            _serviceManager = new ServiceManager();
            _serviceContainer = _serviceManager.CreateServiceContainer(ServiceScope.Global);
            _serviceContainer.AddService<IServiceManager>(_serviceManager);
            _serviceContainer.AddService<IHost>(this);

            _contextService = new ContextService(this);
            _symbolService = new SymbolService(this);
            _serviceContainer.AddService<IContextService>(_contextService);
            _serviceContainer.AddService<ISymbolService>(_symbolService);

            // Register all the services and commands in the Microsoft.Diagnostics.DebugServices.Implementation assembly
            _serviceManager.LoadExtension(typeof(Target).Assembly);

            // Automatically enable symbol server support
            _symbolService.AddSymbolServer(msdl: true, symweb: false, timeoutInMinutes: 6, retryCount: 5);
            _symbolService.AddCachePath(_symbolService.DefaultSymbolCache);
        }

        protected override ITarget GetTarget()
        {
            _dataTarget = DataTarget.LoadDump(DumpFile);

            OSPlatform targetPlatform = _dataTarget.DataReader.TargetPlatform;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                targetPlatform = OSPlatform.OSX;
            }
            _symbolService.AddDirectoryPath(Path.GetDirectoryName(DumpFile));
            return new TargetFromDataReader(_dataTarget.DataReader, targetPlatform, this, _targetIdFactory++, DumpFile);
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType => HostType.DotnetDump;

        public IServiceProvider Services => _serviceContainer.Services;

        public IEnumerable <ITarget> EnumerateTargets() => Target != null ? new ITarget[] { Target } : Array.Empty<ITarget>();

        #endregion
    }
}
