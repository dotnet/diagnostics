// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static Microsoft.Diagnostics.ExtensionCommands.DacCommandHelpers;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpassembly", Aliases = new[] { "DumpAssembly" }, Help = "Displays details about an assembly.")]
    public sealed class DumpAssemblyCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "Assembly address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            if (!TryParseAddressOrExpression(Address, out ulong address) || address == 0)
            {
                throw new DiagnosticsException($"Invalid Assembly {Address}");
            }

            int pointerSize = memory.PointerSize;
            address = ToClrDataAddress(address, pointerSize);

            ISOSDacInterface dac = Dac;
            DacpAssemblyData assembly = default;
            if (dac.GetAssemblyData(0, address, &assembly) != 0)
            {
                throw new DiagnosticsException("Fail to fill Assembly");
            }

            Write("Parent Domain:      ");
            WriteDacAddressLink(Console, assembly.ParentDomain, pointerSize, "DumpDomain", DmlEnabled);
            WriteLine();

            WriteLine("Name:               {0}", GetAssemblyName(dac, address) ?? "Unknown");

            WriteAssemblyModules(Console, dac, assembly, modules, target.OperatingSystem, pointerSize, DmlEnabled, forDumpDomain: false);
        }
    }
}
