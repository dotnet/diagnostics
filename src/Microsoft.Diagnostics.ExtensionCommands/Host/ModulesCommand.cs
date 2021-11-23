// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "modules", Aliases = new string[] { "lm" }, Help = "Displays the native modules in the process.")]
    public class ModulesCommand : CommandBase
    {
        [Option(Name = "--verbose", Aliases = new string[] { "-v" }, Help = "Displays more details.")]
        public bool Verbose { get; set; }

        public IModuleService ModuleService { get; set; }

        public override void Invoke()
        {
            ulong totalSize = 0;
            foreach (IModule module in ModuleService.EnumerateModules().OrderBy((m) => m.ModuleIndex))
            {
                totalSize += module.ImageSize;
                if (Verbose)
                {
                    WriteLine("{0} {1}", module.ModuleIndex, module.FileName);
                    WriteLine("    Address:         {0:X16}", module.ImageBase);
                    WriteLine("    ImageSize:       {0:X8}", module.ImageSize);
                    WriteLine("    IsPEImage:       {0}", module.IsPEImage);
                    WriteLine("    IsManaged:       {0}", module.IsManaged);
                    WriteLine("    IsFileLayout:    {0}", module.IsFileLayout?.ToString() ?? "<unknown>");
                    WriteLine("    IndexFileSize:   {0}", module.IndexFileSize?.ToString("X8") ?? "<none>");
                    WriteLine("    IndexTimeStamp:  {0}", module.IndexTimeStamp?.ToString("X8") ?? "<none>");
                    WriteLine("    Version:         {0}", module.VersionData?.ToString() ?? "<none>");
                    string versionString = module.VersionString;
                    if (!string.IsNullOrEmpty(versionString)) {
                        WriteLine("                     {0}", versionString);
                    }
                    WriteLine("    PdbInfo:         {0}", module.PdbFileInfo?.ToString() ?? "<none>");
                    WriteLine("    BuildId:         {0}", !module.BuildId.IsDefaultOrEmpty ? string.Concat(module.BuildId.Select((b) => b.ToString("x2"))) : "<none>");
                }
                else
                {
                    WriteLine("{0:X16} {1:X8} {2}", module.ImageBase, module.ImageSize, module.FileName);
                }
            }
            WriteLine("Total image size: {0}", totalSize);
        }
    }
}
