// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public sealed class CoreModule : IDiagnosticsModule
    {
        public void RegisterServices(IServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            registry.AddService<IDataReader, DataReader>(ServiceScope.Target);
            registry.AddService<IClrSymbolProvider, HostSymbolProvider>(ServiceScope.Target);
            registry.AddService<IRuntimeService, RuntimeService>(ServiceScope.Target);
            registry.AddProvider<IRuntimeProvider, RuntimeProvider>();
            registry.AddServiceFactory<PEModule>(ServiceScope.Module, services => PEModule.CreatePEModule(
                services.GetRequiredService<IModule>(),
                services.GetRequiredService<ISymbolService>()));
            registry.AddServiceFactory<ELFModule>(ServiceScope.Module, services => ELFModule.CreateELFModule(
                services.GetRequiredService<IModule>(),
                services.GetRequiredService<ISymbolService>()));
            registry.AddServiceFactory<MachOModule>(ServiceScope.Module, services => MachOModule.CreateMachOModule(
                services.GetRequiredService<ISymbolService>(),
                services.GetRequiredService<IModule>()));
            registry.AddServiceFactory<ELFFile>(ServiceScope.Module, services => Utilities.CreateELFFile(
                services.GetRequiredService<IMemoryService>(),
                services.GetRequiredService<IModule>()));
            registry.AddServiceFactory<MachOFile>(ServiceScope.Module, services => Utilities.CreateMachOFile(
                services.GetRequiredService<IMemoryService>(),
                services.GetRequiredService<IModule>()));
        }

        public void RegisterCommands(ICommandService commandService)
        {
            if (commandService is null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }
        }
    }
}
