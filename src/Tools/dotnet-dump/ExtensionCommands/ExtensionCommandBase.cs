// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Repl;

namespace Microsoft.Diagnostic.Tools.Dump.ExtensionCommands
{
    public abstract class ExtensionCommandBase : CommandBase
    {
        /// <summary>
        /// Helper bound to the current ClrRuntime that provides
        /// high level services on top of ClrMD.
        /// </summary>
        public ClrMDHelper Helper { get; set; }

        /// <summary>
        /// Display text
        /// </summary>
        /// <param name="message">text message</param>
        protected void Write(string message)
        {
            Console.Write(message);
        }

        [HelpInvoke]
        public void InvokeHelp()
        {
            WriteLine(GetDetailedHelp());
        }

        protected virtual string GetDetailedHelp()
        {
            return string.Empty;
        }
    }
}
