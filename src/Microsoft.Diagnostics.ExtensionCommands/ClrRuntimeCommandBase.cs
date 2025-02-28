// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public abstract class ClrRuntimeCommandBase : CommandBase
    {
        public const string RuntimeNotFoundMessage = $"No CLR runtime found.\nThis means that a .NET runtime module or the DAC for the runtime can not be found, loaded or downloaded.\nFor more information see https://go.microsoft.com/fwlink/?linkid=2135652";

        [ServiceImport(Optional = true)]
        public ClrRuntime Runtime { get; set; }

        [FilterInvoke(Message = RuntimeNotFoundMessage)]
        public static bool FilterInvoke([ServiceImport(Optional = true)] ClrRuntime runtime) => runtime != null;
    }
}
