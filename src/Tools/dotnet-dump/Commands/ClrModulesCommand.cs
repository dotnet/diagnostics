// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using System.CommandLine;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "clrmodules", Help = "Lists the managed modules in the process.")]
    public class ClrModulesCommand : CommandBase
    {
        public AnalyzeContext AnalyzeContext { get; set; }

        public override void Invoke()
        {
            foreach (ClrModule module in AnalyzeContext.Runtime.Modules)
            {
                WriteLine("{0:X16} {1}", module.Address, module.FileName);
            }
        }
    }
}
