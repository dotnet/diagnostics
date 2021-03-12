// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "sosstatus", Help = "Display internal status or reset the internal cached state.")]
    public class StatusCommand : CommandBase
    {
        public ITarget Target { get; set; }

        public ISymbolService SymbolService { get; set; }

        [Option(Name = "-reset", Help = "Reset all the cached internal state.")]
        public bool Reset { get; set; }

        public override void Invoke()
        {
            if (Reset)
            {
                Target.Flush();
                WriteLine("Internal cached state reset");
            }
            else
            {
                Write(Target.ToString());
                Write(SymbolService.ToString());
            }
        }
    }
}
