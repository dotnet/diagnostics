// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace SOS.Hosting
{
    /// <summary>
    /// Registers native SOS hosting services and commands.
    /// </summary>
    public sealed class SOSHostingModule : IDiagnosticsModule
    {
        public void RegisterServices(IServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.AddService<IHostAssetResolver, HostAssetResolver>(ServiceScope.Global);
            registry.AddService<IClrDataProcessActivator, ClrDataProcessActivator>(ServiceScope.Global);
            registry.AddServiceFactory<SOSLibrary>(ServiceScope.Global, services => SOSLibrary.TryCreate(
                services.GetRequiredService<IHost>(),
                services.GetRequiredService<IHostAssetResolver>(),
                services.GetService<SOSLibrary.ISOSModule>()));
            registry.AddService<SOSHost>(ServiceScope.Target);
            registry.AddService<TargetWrapper>(ServiceScope.Target);
            registry.AddService<RuntimeWrapper>(ServiceScope.Runtime);
        }

        public void RegisterCommands(ICommandService commandService)
        {
            if (commandService is null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }

            commandService.AddCommands<SOSCommand>();
            commandService.AddCommands<WindowsSOSCommand>();
        }
    }
}
