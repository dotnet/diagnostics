// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public abstract class ClrMDHelperCommandBase : CommandBase
    {
        protected ClrMDHelperCommandBase(IServiceProvider services)
            : base(services)
        {
            Helper = services.GetService<ClrMDHelper>();
        }

        /// <summary>
        /// Helper bound to the current ClrRuntime that provides high level services on top of ClrMD.
        /// </summary>
        public ClrMDHelper Helper { get; }

        [FilterInvoke(Message = ClrRuntimeCommandBase.RuntimeNotFoundMessage)]
        public static bool FilterInvoke(IServiceProvider services) => services.GetService<ClrMDHelper>() != null;
    }
}
