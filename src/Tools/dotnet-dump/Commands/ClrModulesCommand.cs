// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using System.CommandLine;

namespace Microsoft.Diagnostics.Tools.Dump
{
    [Command(Name = "clrmodules", Help = "Lists the managed modules in the process.")]
    public class ClrModulesCommand : CommandBase
    {
        public ClrRuntime Runtime { get; set; }

        public DataTarget DataTarget { get; set; }

        [Option(Name = "--verbose", Help = "Displays detailed information about the modules.")]
        [OptionAlias(Name = "-v")]
        public bool Verbose { get; set; }

        public override void Invoke()
        {
            if (Runtime == null)
            {
                throw new DiagnosticsException("No CLR runtime set");
            }
            foreach (ClrModule module in Runtime.EnumerateModules())
            {
                if (Verbose)
                {
                    WriteLine("{0}", module.Name);
                    WriteLine("    AssemblyName:    {0}", module.AssemblyName);
                    WriteLine("    ImageBase:       {0:X16}", module.ImageBase);
                    WriteLine("    Size:            {0:X8}", module.Size);
                    WriteLine("    Address:         {0:X16}", module.Address);
                    WriteLine("    IsPEFile:        {0}", module.IsPEFile);
                    WriteLine("    Layout:          {0}", module.Layout);
                    WriteLine("    IsDynamic:       {0}", module.IsDynamic);
                    WriteLine("    MetadataAddress: {0:X16}", module.MetadataAddress);
                    WriteLine("    MetadataSize:    {0:X16}", module.MetadataLength);
                    WriteLine("    PdbInfo:         {0}", module.Pdb?.ToString() ?? "<none>");

                    DataTarget.DataReader.GetVersionInfo(module.ImageBase, out VersionInfo version);
                    WriteLine("    Version:         {0}", version);
                }
                else
                {
                    WriteLine("{0:X16} {1:X8} {2}", module.ImageBase, module.Size, module.Name);
                }
            }
        }
    }
}
