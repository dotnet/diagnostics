// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "modules", Help = "Displays the native modules in the process.")]
    [CommandAlias(Name = "lm")]
    public class ModulesCommand : CommandBase
    {
        [Option(Name = "--verbose", Help = "Displays more details.")]
        [OptionAlias(Name = "-v")]
        public bool Verbose { get; set; }

        public DataTarget DataTarget { get; set; }

        public override void Invoke()
        {
            foreach (ModuleInfo module in DataTarget.DataReader.EnumerateModules())
            {
                if (Verbose)
                {
                    WriteLine("{0}", module.FileName);
                    WriteLine("    Address:   {0:X16}", module.ImageBase);
                    WriteLine("    IsManaged: {0}", module.IsManaged);
                    WriteLine("    FileSize:  {0:X8}", module.FileSize);
                    WriteLine("    TimeStamp: {0:X8}", module.TimeStamp);
                    WriteLine("    Version:   {0}", module.Version);
                    WriteLine("    PdbInfo:   {0}", module.Pdb?.ToString() ?? "<none>");
                    if (module.BuildId != null) {
                        WriteLine("    BuildId:   {0}", string.Concat(module.BuildId.Select((b) => b.ToString("x2"))));
                    }
                }
                else
                {
                    WriteLine("{0:X16} {1:X8} {2}", module.ImageBase, module.FileSize, module.FileName);
                }
            }
        }
    }
}
