// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public static class CommandFormatHelpers
    {
        public const string DacTableSymbol = "g_dacTable";
        public const string DebugHeaderSymbol = "DotNetRuntimeDebugHeader";

        /// <summary>
        /// Displays the special diagnostics info header memory block (.NET Core 8 or later on Linux/MacOS)
        /// </summary>
        public static void DisplaySpecialInfo(this CommandBase command, string indent = "")
        {
            if (command.Services.GetService<ITarget>().OperatingSystem != OSPlatform.Windows)
            {
                ulong address = SpecialDiagInfoHeader.GetAddress(command.Services);
                command.Console.Write($"{indent}SpecialDiagInfoHeader   : {address:X16}");
                if (SpecialDiagInfoHeader.TryRead(command.Services, address, out SpecialDiagInfoHeader info))
                {
                    command.Console.WriteLine(info.IsValid ? "" : " <INVALID>");
                    command.Console.WriteLine($"{indent}    Signature:              {info.Signature}");
                    command.Console.WriteLine($"{indent}    Version:                {info.Version}");
                    command.Console.WriteLine($"{indent}    ExceptionRecordAddress: {info.ExceptionRecordAddress:X16}");
                    command.Console.WriteLine($"{indent}    RuntimeBaseAddress:     {info.RuntimeBaseAddress:X16}");

                    if (info.Version >= SpecialDiagInfoHeader.SPECIAL_DIAGINFO_RUNTIME_BASEADDRESS)
                    {
                        IModule runtimeModule = command.Services.GetService<IModuleService>().GetModuleFromBaseAddress(info.RuntimeBaseAddress);
                        if (runtimeModule != null)
                        {
                            command.DisplayRuntimeExports(runtimeModule, error: true, indent + "    ");
                        }
                    }
                }
                else
                {
                    command.Console.WriteLine(" <NONE>");
                }
            }
        }

        /// <summary>
        /// Display the module's resources. The ClrDebugResource is pretty formatted.
        /// </summary>
        /// <exception cref="DiagnosticsException"></exception>
        public static void DisplayResources(this CommandBase command, IModule module, bool all, string indent)
        {
            if (module.IsPEImage)
            {
                command.Console.WriteLine($"{indent}Resources:");
                IDataReader reader = command.Services.GetService<IDataReader>() ?? throw new DiagnosticsException("IDataReader service needed");
                IResourceNode resourceRoot = ModuleInfo.TryCreateResourceRoot(reader, module.ImageBase, module.ImageSize, module.IsFileLayout.GetValueOrDefault(false));
                if (resourceRoot != null)
                {
                    foreach (IResourceNode child in resourceRoot.Children)
                    {
                        DisplayResources(command.Console, child, all, indent + "    ");
                    }
                }
            }
        }

        private static void DisplayResources(IConsoleService console, IResourceNode resourceNode, bool all, string indent)
        {
            if (resourceNode.Name.StartsWith("CLRDEBUGINFO"))
            {
                console.WriteLine($"{indent}Name: {resourceNode.Name}");
                IResourceNode node = resourceNode.Children.FirstOrDefault();
                if (node is not null)
                {
                    ClrDebugResource clrDebugResource = node.Read<ClrDebugResource>(0);
                    console.WriteLine($"{indent}    Size:           {node.Size:X8}");
                    console.WriteLine($"{indent}    Version:        {clrDebugResource.dwVersion:X8}");
                    console.WriteLine($"{indent}    Signature:      {clrDebugResource.signature}");
                    console.WriteLine($"{indent}    DacTimeStamp:   {clrDebugResource.dwDacTimeStamp:X8}");
                    console.WriteLine($"{indent}    DacSizeOfImage: {clrDebugResource.dwDacSizeOfImage:X8}");
                    console.WriteLine($"{indent}    DbiTimeStamp:   {clrDebugResource.dwDbiTimeStamp:X8}");
                    console.WriteLine($"{indent}    DbiSizeOfImage: {clrDebugResource.dwDbiSizeOfImage:X8}");
                }
            }
            else
            {
                if (all)
                {
                    console.WriteLine($"{indent}Name: {resourceNode.Name}");
                    int size = resourceNode.Size;
                    if (size > 0)
                    {
                        console.WriteLine($"{indent}Size: {size:X8}");
                    }
                    indent += "    ";
                }
                foreach (IResourceNode child in resourceNode.Children)
                {
                    DisplayResources(console, child, all, indent);
                }
            }
        }

        /// <summary>
        /// Displays the module's special runtime exports
        /// </summary>
        public static void DisplayRuntimeExports(this CommandBase command, IModule module, bool error, string indent)
        {
            bool header = false;
            IConsoleService Console()
            {
                if (!header)
                {
                    header = true;
                    command.Console.WriteLine($"{indent}Exports:");
                    indent += "    ";
                }
                return command.Console;
            }
            // Print the runtime info (.NET Core single-file)
            IExportSymbols symbols = module.Services.GetService<IExportSymbols>();
            if (symbols != null && symbols.TryGetSymbolAddress(RuntimeInfo.RUNTIME_INFO_SYMBOL, out ulong infoAddress))
            {
                Console().Write($"{indent}{RuntimeInfo.RUNTIME_INFO_SYMBOL,-24}: {infoAddress:X16}");
                if (RuntimeInfo.TryRead(command.Services, infoAddress, out RuntimeInfo info))
                {
                    Console().WriteLine(info.IsValid ? "" : " <INVALID>");
                    Console().WriteLine($"{indent}    Signature:                  {info.Signature}");
                    Console().WriteLine($"{indent}    Version:                    {info.Version}");
                    Console().WriteLine($"{indent}    RuntimeModuleIndex:         {info.RawRuntimeModuleIndex.ToHex()}");
                    Console().WriteLine($"{indent}    DacModuleIndex:             {info.RawDacModuleIndex.ToHex()}");
                    Console().WriteLine($"{indent}    DbiModuleIndex:             {info.RawDbiModuleIndex.ToHex()}");
                    if (module.IsPEImage)
                    {
                        Console().WriteLine($"{indent}    RuntimePEIndex:             {info.RuntimePEIIndex.timeStamp:X8}/{info.RuntimePEIIndex.fileSize:X}");
                        Console().WriteLine($"{indent}    DacPEIndex:                 {info.DacPEIndex.timeStamp:X8}/{info.DacPEIndex.fileSize:X}");
                        Console().WriteLine($"{indent}    DbiPEIndex:                 {info.DbiPEIndex.timeStamp:X8}/{info.DbiPEIndex.fileSize:X}");
                    }
                    else
                    {
                        Console().WriteLine($"{indent}    RuntimeBuildId:             {info.RuntimeBuildId.ToHex()}");
                        Console().WriteLine($"{indent}    DacBuildId:                 {info.DacBuildId.ToHex()}");
                        Console().WriteLine($"{indent}    DbiBuildId:                 {info.DbiBuildId.ToHex()}");
                    }
                    Console().WriteLine($"{indent}    RuntimeVersion:             {info.RuntimeVersion?.ToString() ?? "<none>"}");
                }
                else
                {
                    Console().WriteLineError(" <NONE>");
                }
            }
            else if (error)
            {
                Console().WriteLineError($"{indent}{RuntimeInfo.RUNTIME_INFO_SYMBOL,-24}: <NO SYMBOL>");
            }

            // Print the Windows runtime engine metrics (.NET Core and .NET Framework)
            if (command.Services.GetService<ITarget>().OperatingSystem == OSPlatform.Windows)
            {
                if (symbols != null && symbols.TryGetSymbolAddress(ClrEngineMetrics.Symbol, out ulong metricsAddress))
                {
                    Console().Write($"{indent}{ClrEngineMetrics.Symbol,-24}: ({metricsAddress:X16})");
                    if (ClrEngineMetrics.TryRead(command.Services, metricsAddress, out ClrEngineMetrics metrics))
                    {
                        Console().WriteLine();
                        Console().WriteLine($"{indent}    Size:                   {metrics.Size} (0x{metrics.Size:X2})");
                        Console().WriteLine($"{indent}    DbiVersion:             {metrics.DbiVersion}");
                        Console().WriteLine($"{indent}    ContinueStartupEvent:   {((ulong)metrics.ContinueStartupEvent):X16}");
                    }
                    else
                    {
                        Console().WriteLineError(" <NONE>");
                    }
                }
                else if (error)
                {
                    Console().WriteLineError($"{indent}{ClrEngineMetrics.Symbol,-24}: <NO SYMBOL>");
                }
            }

            // Print the DAC table address (g_dacTable)
            if (symbols != null && symbols.TryGetSymbolAddress(DacTableSymbol, out ulong dacTableAddress))
            {
                Console().WriteLine($"{indent}{DacTableSymbol,-24}: {dacTableAddress:X16}");
            }
            else if (error)
            {
                Console().WriteLineError($"{indent}{DacTableSymbol,-24}: <NO SYMBOL>");
            }

            // Print the Native AOT contract data address (DotNetRuntimeDebugHeader)
            if (symbols != null && symbols.TryGetSymbolAddress(DebugHeaderSymbol, out ulong debugHeaderAddress))
            {
                Console().WriteLine($"{indent}{DebugHeaderSymbol,-24}: {debugHeaderAddress:X16}");
            }
            else if (error)
            {
                Console().WriteLineError($"{indent}{DebugHeaderSymbol,-24}: <NO SYMBOL>");
            }
        }
    }
}
