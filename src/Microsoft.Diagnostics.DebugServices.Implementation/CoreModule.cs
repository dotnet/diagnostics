// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats;
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
            registry.AddServiceFactory<Reader>(ServiceScope.Target, CreateReader);
            registry.AddService<IClrSymbolProvider, HostSymbolProvider>(ServiceScope.Target);
            registry.AddService<IRuntimeService, RuntimeService>(ServiceScope.Target);
            registry.AddProvider<IRuntimeProvider, DescriptionBasedRuntimeProvider>();
            registry.AddProvider<IRuntimeDescriptionProvider, DotNetRuntimeDiscoveryProvider>();
            registry.AddProvider<IRuntimeDescriptionProvider, SingleFileRuntimeDiscoveryProvider>();
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

        private static Reader CreateReader(IServiceProvider services)
        {
            IMemoryService memoryService = services.GetRequiredService<IMemoryService>();
            Stream stream = memoryService.CreateMemoryStream();
            LayoutManager layoutManager = new LayoutManager()
                .AddPrimitives()
                .AddEnumTypes()
                .AddSizeT(memoryService.PointerSize)
                .AddNullTerminatedString();
            foreach (Func<IServiceProvider, Action<LayoutManager>> factory in
                services.GetRequiredService<IServiceManager>().EnumerateProviderFactories<Action<LayoutManager>>())
            {
                factory(services)(layoutManager);
            }
            return new Reader(new StreamAddressSpace(stream), layoutManager);
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
