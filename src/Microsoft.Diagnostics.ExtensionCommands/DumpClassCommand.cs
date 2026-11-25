// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpclass", Help = "Displays information about a EE class structure at the specified address.")]
    public sealed class DumpClassCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "EEClass or canonical method table address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "/d", Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            if (WarnIfMinidumpSafeMode())
            {
                return;
            }
            if (Address is null)
            {
                throw new DiagnosticsException("Missing EEClass address");
            }
            ClrDataAddress methodTable = 0;
            if (!TryParseAddressOrExpression(Address, out ulong address) || Dac.GetMethodTableForEEClass(ToClrDataAddress(address, memory.PointerSize), &methodTable) < 0)
            {
                throw new DiagnosticsException("Invalid EEClass address");
            }

            bool canonical = ToClrDataAddress(address, memory.PointerSize) == methodTable;
            DacpMethodTableData type = default;
            if (Dac.GetMethodTableData(methodTable, &type) != 0)
            {
                throw new DiagnosticsException("EEClass has an invalid MethodTable address");
            }
            DacObjectRenderer renderer = new(Dac, Console, memory, modules, target.OperatingSystem, UseDml(target.Host.HostType, DmlEnabled));
            Console.WriteLine($"Class Name:      {DacCommandHelpers.GetMethodTableName(Dac, methodTable) ?? "<error>"}");
            Console.WriteLine($"mdToken:         {DacCommandHelpers.FormatDacAddress(type.cl, memory.PointerSize, upperCase: target.OperatingSystem == System.Runtime.InteropServices.OSPlatform.Windows)}");
            Console.WriteLine($"File:            {DacCommandHelpers.GetModuleName(Dac, type.module, modules, target.OperatingSystem, memory.PointerSize)}");

            DacpMethodTableData parent = default;
            if (type.parentMethodTable != 0 && Dac.GetMethodTableData(type.parentMethodTable, &parent) != 0)
            {
                throw new DiagnosticsException("EEClass has an invalid MethodTable address");
            }
            if (canonical)
            {
                renderer.WriteAddress("Parent:    ", type.parentMethodTable, "DumpMT");
            }
            else
            {
                renderer.WriteAddress("Parent Class:    ", parent.klass, "DumpClass");
            }
            renderer.WriteAddress("Module:          ", type.module, "DumpModule");
            renderer.WriteAddress("Method Table:    ", methodTable, "DumpMT");
            if (canonical && methodTable != type.klass)
            {
                renderer.WriteAddress("Canonical:    ", type.klass, "DumpMT");
            }

            WriteClassDetails(Dac, Console, renderer, methodTable, type);
        }

        internal static unsafe void WriteClassDetails(ISOSDacInterface dac, IConsoleService console, DacObjectRenderer renderer, ulong methodTable, DacpMethodTableData type)
        {
            TypeAttributes attributes = (TypeAttributes)type.dwAttrClass;
            console.Write($"Class Attributes:    {type.dwAttrClass:x8}  ");
            if ((attributes & TypeAttributes.Interface) != 0)
            {
                console.Write("Interface ");
            }
            if ((attributes & TypeAttributes.Abstract) != 0)
            {
                console.Write("Abstract ");
            }
            if ((attributes & TypeAttributes.Import) != 0)
            {
                console.Write("ComImport ");
            }
            if ((attributes & TypeAttributes.Sealed) != 0)
            {
                console.Write("Sealed ");
            }
            console.WriteLine();
            if (type.wNumVirtuals != 0)
            {
                console.WriteLine($"Vtable Slots:    {type.wNumVirtuals}");
            }
            if (type.wNumVtableSlots != 0)
            {
                console.WriteLine($"Total Method Slots:  {type.wNumVtableSlots}");
            }
            DacpMethodTableFieldData fields = default;
            if (dac.GetMethodTableFieldData(methodTable, &fields) < 0)
            {
                return;
            }
            console.WriteLine($"NumInstanceFields:   {fields.wNumInstanceFields}");
            console.WriteLine($"NumStaticFields:     {fields.wNumStaticFields}");
            if (fields.wNumThreadStaticFields != 0)
            {
                console.WriteLine($"NumThreadStaticFields: {fields.wNumThreadStaticFields}");
            }
            if (fields.wContextStaticsSize != 0)
            {
                console.WriteLine($"ContextStaticOffset: 0x{fields.wContextStaticOffset:x}");
                console.WriteLine($"ContextStaticsSize:  {fields.wContextStaticsSize}");
            }
            if (fields.wNumInstanceFields != 0 || fields.wNumStaticFields != 0)
            {
                renderer.WriteFields(0, methodTable, type, showHeading: false);
            }
        }
    }
}
