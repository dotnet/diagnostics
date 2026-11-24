// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using SOS.Hosting.DbgEng.Interop;

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
            if (IsMinidumpSafeMode())
            {
                Console.WriteLine("This command is not supported in a minidump without full memory");
                Console.WriteLine("To try the command anyway, run !MinidumpMode 0");
                return;
            }

            if (!TryParseAddressOrExpression(Address, out ulong address) || address == 0)
            {
                throw new DiagnosticsException($"Invalid parameter {Address}{(target.Host.HostType == HostType.DotnetDump ? "\n" : "")}");
            }

            bool dml = target.Host.HostType == HostType.DbgEng && (DmlEnabled || Console.SupportsDml);
            new DacObjectRenderer(Dac, Console, memory, modules, target.OperatingSystem, dml).WriteObject(address, !NoFields, target.Host.HostType);
            if (References)
            {
                new DumpObjGCRefsCommand(Services).WriteReferences(address);
            }
        }

        private bool IsMinidumpSafeMode()
        {
            INativeDebuggerDumpService settings = Services.GetService<INativeDebuggerDumpService>();
            INativeDebugControl2 control = Services.GetService<INativeDebugControl2>();
            return settings?.IsMinidumpSafeModeEnabled() == 0 &&
                control is not null &&
                control.GetDebuggeeType(out _, out DEBUG_CLASS_QUALIFIER qualifier) >= 0 &&
                qualifier == DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP &&
                control.GetDumpFormatFlags(out DEBUG_FORMAT flags) >= 0 &&
                (flags & DEBUG_FORMAT.USER_SMALL_FULL_MEMORY) == 0;
        }
    }
}
