// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System.IO;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "setclrpath", Help = "Set the path to load coreclr DAC/DBI files.")]
    public class SetClrPath: CommandBase
    {
        public IRuntimeService RuntimeService { get; set; }

        [Argument(Name = "path", Help = "Runtime directory path.")]
        public string Argument { get; set; }

        [Option(Name = "--clear", Aliases = new string[] { "-c" }, Help = "Clears the runtime directory path.")]
        public bool Clear { get; set; }

        public override void Invoke()
        {
            if (RuntimeService == null)
            {
                throw new DiagnosticsException("Runtime service required");
            }
            if (Clear)
            {
                RuntimeService.RuntimeModuleDirectory = null;
            }
            else if (Argument == null)
            {
                WriteLine("Load path for DAC/DBI: '{0}'", RuntimeService.RuntimeModuleDirectory ?? "<none>");
            }
            else
            {
                RuntimeService.RuntimeModuleDirectory = Path.GetFullPath(Argument);
                WriteLine("Set load path for DAC/DBI to '{0}'", RuntimeService.RuntimeModuleDirectory);
            }
        }
    }
}
