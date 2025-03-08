// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestDump : TestHost, ISettingsService
    {
        private readonly Host _host;
        private readonly ContextService _contextService;
        private readonly CommandService _commandService;
        private readonly SymbolService _symbolService;
        private readonly DumpTargetFactory _dumpTargetFactory;

        public TestDump(TestConfiguration config)
            : base(config)
        {
            _host = new Host(HostType.DotnetDump);

            // Loading extensions or adding service factories not allowed after this point.
            ServiceContainer serviceContainer = _host.CreateServiceContainer();

            _contextService = new(_host);
            serviceContainer.AddService<IContextService>(_contextService);
            serviceContainer.AddService<ISettingsService>(this);

            _commandService = new();
            serviceContainer.AddService<ICommandService>(_commandService);

            _symbolService = new(_host);
            serviceContainer.AddService<ISymbolService>(_symbolService);

            _dumpTargetFactory = new DumpTargetFactory(_host);
            serviceContainer.AddService<IDumpTargetFactory>(_dumpTargetFactory);

            // Automatically enable symbol server support
            _symbolService.AddSymbolServer(timeoutInMinutes: 6, retryCount: 5);
            _symbolService.AddCachePath(_symbolService.DefaultSymbolCache);
        }

        public ServiceContainer ServiceContainer => _host.ServiceContainer;

        public CommandService CommandService => _commandService;

        public override IReadOnlyList<string> ExecuteHostCommand(string commandLine) => _commandService.ExecuteAndCapture(commandLine, _contextService.Services);

        protected override ITarget GetTarget()
        {
            _symbolService.AddDirectoryPath(Path.GetDirectoryName(DumpFile));
            return _dumpTargetFactory.OpenDump(DumpFile);
        }

        #region ISettingsService

        public bool DacSignatureVerificationEnabled { get; set; }

        #endregion
    }
}
