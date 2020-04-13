// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;
using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "setclrpath", Help = "Set the path to load coreclr DAC/DBI files.")]
    public class SetClrPath: CommandBase
    {
        public AnalyzeContext AnalyzeContext { get; set; }

        [Argument(Name = "clrpath", Help = "Runtime directory path.")]
        public string Argument { get; set; }

        public override void Invoke()
        {
            if (Argument == null)
            {
                WriteLine("Load path for DAC/DBI: '{0}'", AnalyzeContext.RuntimeModuleDirectory ?? "<none>");
            }
            else
            {
                AnalyzeContext.RuntimeModuleDirectory = Argument;
                WriteLine("Set load path for DAC/DBI to '{0}'", AnalyzeContext.RuntimeModuleDirectory);
            }
        }
    }
}
