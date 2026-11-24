// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpalc", Help = "Displays details about a collectible AssemblyLoadContext into which the specified object is loaded.")]
    public sealed class DumpAlcCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "Object address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            if (WarnIfMinidumpSafeMode())
            {
                return;
            }
            HostType hostType = target.Host.HostType;
            if (!TryParseAddressOrExpression(Address, out ulong address) || address == 0)
            {
                throw new DiagnosticsException($"Invalid parameter {Address}{(hostType == HostType.DotnetDump ? "\n" : "")}");
            }

            DacObjectRenderer renderer = new(Dac, Console, memory, modules, target.OperatingSystem, UseDml(hostType, DmlEnabled));
            DacpObjectData data = renderer.ReadObject(address, hostType, warnOnInvalidGc: false, out _);
            if (data.ObjectType == DacpObjectType.OBJ_FREE)
            {
                renderer.WriteFreeObject(data.Size);
                return;
            }

            ClrDataAddress context = 0;
            if (Dac is ISOSDacInterface8 dac8 && dac8.GetAssemblyLoadContext(data.MethodTable, &context) < 0)
            {
                throw new DiagnosticsException("Failed to get the AssemblyLoadContext");
            }
            if (context == 0)
            {
                Console.WriteLine("Name:        System.Runtime.Loader.DefaultAssemblyLoadContext");
                Console.WriteLine("The managed instance of this context doesn't exist yet");
                return;
            }

            renderer.WriteObject(ToTargetAddress(context, memory.PointerSize), printFields: true, hostType);
        }
    }
}
