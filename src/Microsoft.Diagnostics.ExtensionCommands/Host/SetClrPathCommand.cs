// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "setclrpath", Help = "Sets the path to load coreclr DAC/DBI files.")]
    public class SetClrPath : CommandBase
    {
        [ServiceImport(Optional = true)]
        public IRuntime Runtime { get; set; }

        [Argument(Name = "path", Help = "Runtime directory path.")]
        public string Argument { get; set; }

        [Option(Name = "--clear", Aliases = new string[] { "-c" }, Help = "Clears the runtime directory path.")]
        public bool Clear { get; set; }

        public override void Invoke()
        {
            if (Runtime == null)
            {
                throw new DiagnosticsException("Runtime required");
            }
            if (Clear)
            {
                Runtime.RuntimeModuleDirectory = null;
            }
            else if (Argument == null)
            {
                WriteLine("Load path for DAC/DBI: '{0}'", Runtime.RuntimeModuleDirectory ?? "<none>");
            }
            else
            {
                Runtime.RuntimeModuleDirectory = Path.GetFullPath(Argument);
                WriteLine("Set load path for DAC/DBI to '{0}'", Runtime.RuntimeModuleDirectory);
            }
        }
    }
}
