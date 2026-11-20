// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public abstract class ClrRuntimeCommandBase : CommandBase
    {
        protected ClrRuntimeCommandBase(IServiceProvider services)
            : base(services)
        {
            Runtime = services.GetService<ClrRuntime>();
        }

        public const string RuntimeNotFoundMessage = $"No CLR runtime found.\nThis means that a .NET runtime module or the DAC for the runtime can not be found, loaded or downloaded.\nFor more information see https://go.microsoft.com/fwlink/?linkid=2135652";

        public ClrRuntime Runtime { get; }

        [FilterInvoke(Message = RuntimeNotFoundMessage)]
        public static bool FilterInvoke(IServiceProvider services) => services.GetService<ClrRuntime>() != null;
    }
}
