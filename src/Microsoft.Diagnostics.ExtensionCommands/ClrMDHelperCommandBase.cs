// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public abstract class ClrMDHelperCommandBase : CommandBase
    {
        /// <summary>
        /// Helper bound to the current ClrRuntime that provides high level services on top of ClrMD.
        /// </summary>
        [ServiceImport(Optional = true)]
        public ClrMDHelper Helper { get; set; }

        [FilterInvoke(Message = ClrRuntimeCommandBase.RuntimeNotFoundMessage)]
        public static bool FilterInvoke([ServiceImport(Optional = true)] ClrMDHelper helper) => helper != null;
    }
}
