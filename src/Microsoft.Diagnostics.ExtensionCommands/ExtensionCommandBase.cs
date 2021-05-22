// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public abstract class ExtensionCommandBase : CommandBase
    {
        /// <summary>
        /// Helper bound to the current ClrRuntime that provides high level services on top of ClrMD.
        /// </summary>
        [ServiceImport(Optional = true)]
        public ClrMDHelper Helper { get; set; }

        public override void Invoke()
        {
            if (Helper == null)
            {
                throw new DiagnosticsException("No CLR runtime set");
            }
            ExtensionInvoke();
        }

        public abstract void ExtensionInvoke();

        [HelpInvoke]
        public void InvokeHelp()
        {
            WriteLine(GetDetailedHelp());
        }

        protected abstract string GetDetailedHelp();
    }
}
