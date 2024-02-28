// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "modules", Aliases = new string[] { "lm" }, Help = "Displays the native modules in the process.")]
    public class ModulesCommand : CommandBase
    {
        [Option(Name = "--verbose", Aliases = new string[] { "-v" }, Help = "Displays more details.")]
        public bool Verbose { get; set; }

        [Option(Name = "--segments", Aliases = new string[] { "-s" }, Help = "Displays the module segments.")]
        public bool Segment { get; set; }

        [Option(Name = "--resources", Aliases = new string[] { "-r" }, Help = "Displays the module resources.")]
        public bool Resources { get; set; }

        [Option(Name = "--name", Aliases = new string[] { "-n" }, Help = "RegEx filter on module name (path not included).")]
        public string ModuleName { get; set; }

        [Option(Name = "--address", Aliases = new string[] { "-a" }, Help = "Lookup address in module list.")]
        public ulong? Address { get; set; }

        [ServiceImport]
        public ITarget Target { get; set; }

        [ServiceImport]
        public IModuleService ModuleService { get; set; }

        [ServiceImport]
        public IMemoryService MemoryService { get; set; }

        public override void Invoke()
        {
            if (Address.HasValue)
            {
                IModule module = ModuleService.GetModuleFromAddress(Address.Value);
                if (module != null)
                {
                    DisplayModule(module);
                }
                else
                {
                    WriteLineError($"Address 0x{Address:X16} not found");
                }
            }
            else
            {
                Regex regex = ModuleName is not null ? new Regex(ModuleName, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : null;
                ulong totalSize = 0;
                foreach (IModule module in ModuleService.EnumerateModules().OrderBy((m) => m.ModuleIndex))
                {
                    totalSize += module.ImageSize;
                    if (regex is null || !string.IsNullOrEmpty(module.FileName) && regex.IsMatch(Path.GetFileName(module.FileName)))
                    {
                        DisplayModule(module);
                    }
                }
                WriteLine("Total image size: {0}", totalSize);
            }
        }

        private void DisplayModule(IModule module)
        {
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
                WriteLine("    Version:         {0}", module.GetVersionData()?.ToString() ?? "<none>");
                string versionString = module.GetVersionString();
                if (!string.IsNullOrEmpty(versionString))
                {
                    WriteLine("                     {0}", versionString);
                }
                foreach (PdbFileInfo pdbFileInfo in module.GetPdbFileInfos())
                {
                    WriteLine("    PdbInfo:         {0}", pdbFileInfo);
                }
                WriteLine("    BuildId:         {0}", !module.BuildId.IsDefaultOrEmpty ? string.Concat(module.BuildId.Select((b) => b.ToString("x2"))) : "<none>");

                this.DisplayRuntimeExports(module, error: false, indent: "    ");
            }
            else
            {
                WriteLine("{0:X16} {1:X8} {2}", module.ImageBase, module.ImageSize, module.FileName);
            }
            if (Segment)
            {
                DisplaySegments(module);
            }
            if (Resources)
            {
                this.DisplayResources(module, all: true, indent: "    ");
            }
        }

        private void DisplaySegments(IModule module)
        {
            try
            {
                ELFFile elfFile = module.Services.GetService<ELFFile>();
                if (elfFile is not null)
                {
                    foreach (ELFProgramHeader programHeader in elfFile.Segments.Select((segment) => segment.Header))
                    {
                        uint flags = MemoryService.PointerSize == 8 ? programHeader.Flags : programHeader.Flags32;
                        ulong loadAddress = programHeader.VirtualAddress;
                        ulong loadSize = programHeader.VirtualSize;
                        ulong fileOffset = programHeader.FileOffset;
                        string type = programHeader.Type.ToString();
                        WriteLine($"        Segment: {loadAddress:X16} {loadSize:X16} {fileOffset:X16} {flags:x2} {type}");
                    }
                }
                else
                {
                    MachOFile machOFile = module.Services.GetService<MachOFile>();
                    if (machOFile is not null)
                    {
                        WriteLine("    LoadAddress:     {0:X16}", machOFile.LoadAddress);
                        WriteLine("    LoadBias:        {0:X16}", machOFile.PreferredVMBaseAddress);
                        for (int i = 0; i < machOFile.Segments.Length; i++)
                        {
                            MachSegment segment = machOFile.Segments[i];
                            ulong loadAddress = segment.LoadCommand.VMAddress;
                            ulong loadSize = segment.LoadCommand.VMSize;
                            ulong fileOffset = segment.LoadCommand.FileOffset;
                            uint prot = segment.LoadCommand.InitProt;
                            string name = segment.LoadCommand.SegName.ToString();
                            WriteLine($"        Segment {i}: {loadAddress:X16} {loadSize:X16} {fileOffset:X16} {prot:x2} {name}");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException)
            {
                Trace.TraceError($"Exception displaying module segments: {ex}");
            }
        }
    }
}
