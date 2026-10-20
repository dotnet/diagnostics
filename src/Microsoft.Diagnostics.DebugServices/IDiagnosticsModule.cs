// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Registers the services, providers, and commands contributed by an assembly.
    /// </summary>
    public interface IDiagnosticsModule
    {
        void RegisterServices(IServiceRegistry registry);

        void RegisterCommands(ICommandService commandService);
    }
}
