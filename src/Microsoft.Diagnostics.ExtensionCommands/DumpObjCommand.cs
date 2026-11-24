// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpobj", Aliases = new[] { "do" }, Help = "Displays info about an object at the specified address.")]
    public sealed class DumpObjCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "Object address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "--no-fields", Aliases = new[] { "-nofields" }, Help = "Omit the field table.")]
        public bool NoFields { get; set; }

        [Option<bool>(Name = "--refs", Aliases = new[] { "-refs" }, Help = "Display GC references from the object.")]
        public bool References { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override void Invoke()
        {
            if (WarnIfMinidumpSafeMode())
            {
                return;
            }

            if (!TryParseAddressOrExpression(Address, out ulong address) || address == 0)
            {
                throw new DiagnosticsException($"Invalid parameter {Address}{(target.Host.HostType == HostType.DotnetDump ? "\n" : "")}");
            }

            bool dml = UseDml(target.Host.HostType, DmlEnabled);
            new DacObjectRenderer(Dac, Console, memory, modules, target.OperatingSystem, dml).WriteObject(address, !NoFields, target.Host.HostType);
            if (References)
            {
                new DumpObjGCRefsCommand(Services).WriteReferences(address);
            }
        }
    }
}
