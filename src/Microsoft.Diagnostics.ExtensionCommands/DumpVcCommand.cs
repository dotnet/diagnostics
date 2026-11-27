// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpvc", Help = "Displays info about the fields of a value class.")]
    public sealed class DumpVcCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string[]>(Name = "addresses", Help = "The method table and value class addresses in hexadecimal or as debugger expressions.")]
        public string[] Addresses { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override void Invoke()
        {
            if (WarnIfMinidumpSafeMode())
            {
                return;
            }
            HostType hostType = target.Host.HostType;
            if (Addresses?.Length != 2)
            {
                string prefix = hostType == HostType.DbgEng ? "!" : "";
                Console.WriteLine($"Usage: {prefix}dumpvc <Method Table> <Value object start addr>");
                return;
            }
            if (!TryParseAddressOrExpression(Addresses[0], out ulong methodTable) || !TryParseAddressOrExpression(Addresses[1], out ulong address))
            {
                throw new DiagnosticsException("Invalid address");
            }

            DacObjectRenderer renderer = new(Dac, Console, memory, modules, target.OperatingSystem, UseDml(hostType, DmlEnabled));
            renderer.WriteValueClass(ToClrDataAddress(methodTable, memory.PointerSize), ToTargetAddress(address, memory.PointerSize), printFields: true);
        }
    }
}
