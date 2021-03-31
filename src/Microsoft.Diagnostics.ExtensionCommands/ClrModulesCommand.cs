// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "clrmodules", Help = "Lists the managed modules in the process.")]
    public class ClrModulesCommand : CommandBase
    {
        public ClrRuntime Runtime { get; set; }

        public IModuleService ModuleService { get; set; }

        [Option(Name = "--verbose", Aliases = new string[] { "-v" }, Help = "Displays detailed information about the modules.")]
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
                    WriteLine("{0}{1}", module.Name, module.IsDynamic ? "(Dynamic)" : "");
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
                    VersionInfo? version = null;
                    try
                    {
                        version = ModuleService.GetModuleFromBaseAddress(module.ImageBase).Version;
                    }
                    catch (DiagnosticsException)
                    {
                    }
                    WriteLine("    Version:         {0}", version?.ToString() ?? "<none>");
                }
                else
                {
                    WriteLine("{0:X16} {1:X8} {2}{3}", module.ImageBase, module.Size, module.Name, module.IsDynamic ? "(Dynamic)" : "");
                }
            }
        }
    }
}
