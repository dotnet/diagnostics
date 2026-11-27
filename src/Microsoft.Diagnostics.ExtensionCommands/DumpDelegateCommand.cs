// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpdelegate", Help = "Displays information about a delegate.")]
    public sealed class DumpDelegateCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "Delegate object address in hexadecimal or a debugger expression.")]
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
            if (Address is null)
            {
                Console.WriteLine($"Usage: {(hostType == HostType.DbgEng ? "!" : "")}dumpdelegate <delegate object address>");
                return;
            }
            DacpObjectData data = default;
            if (!TryParseAddressOrExpression(Address, out ulong address) || Dac.GetObjectData(ToClrDataAddress(address, memory.PointerSize), &data) < 0)
            {
                Console.WriteLine("Invalid object.");
                return;
            }

            bool dml = UseDml(hostType, DmlEnabled);
            DacObjectRenderer renderer = new(Dac, Console, memory, modules, target.OperatingSystem, dml);
            if (!IsDelegate(data.MethodTable))
            {
                Console.Write($"Object of type '{DacCommandHelpers.GetObjectTypeName(Dac, address, memory.PointerSize)}' is not a delegate.");
                return;
            }

            Console.WriteLine("Target           Method           Name");
            // The native command prints multicast entries in reverse order.
            Stack<(ulong Address, bool Exit)> pending = new();
            HashSet<ulong> expanded = new();
            pending.Push((ToTargetAddress(address, memory.PointerSize), false));
            Span<char> name = stackalloc char[2048];
            while (pending.TryPop(out (ulong Address, bool Exit) entry))
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                if (entry.Exit)
                {
                    expanded.Remove(entry.Address);
                    continue;
                }
                if (Dac.GetObjectData(ToClrDataAddress(entry.Address, memory.PointerSize), &data) != 0)
                {
                    continue;
                }

                int count = 0;
                bool oldLayout = renderer.TryReadInstancePointer(entry.Address, data.MethodTable, "_invocationList", out ulong list, searchParents: true);
                if (oldLayout)
                {
                    if (renderer.TryGetInstanceFieldAddress(entry.Address, data.MethodTable, "_invocationCount", out ulong countAddress, searchParents: true) &&
                        memory.ReadDword(countAddress, out uint rawCount))
                    {
                        count = unchecked((int)rawCount);
                    }
                }
                else if (renderer.TryReadInstancePointer(entry.Address, data.MethodTable, "_helperObject", out list, searchParents: true) &&
                    renderer.TryReadInstancePointer(entry.Address, data.MethodTable, "_extraData", out ulong extraData, searchParents: true))
                {
                    count = unchecked((int)extraData);
                }

                DacpObjectData array = default;
                if (list != 0 && count > 0 && Dac.GetObjectData(ToClrDataAddress(list, memory.PointerSize), &array) == 0 &&
                    array.ObjectType == DacpObjectType.OBJ_ARRAY && (ulong)count <= array.dwNumComponents && array.dwComponentSize == (uint)memory.PointerSize)
                {
                    if (!expanded.Add(entry.Address))
                    {
                        throw new DiagnosticsException("Cycle in delegate invocation lists");
                    }
                    pending.Push((entry.Address, true));
                    ulong items = ToTargetAddress(array.ArrayDataPtr, memory.PointerSize);
                    for (int i = 0; i < count; i++)
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();
                        if (memory.ReadPointer(items + (ulong)i * array.dwComponentSize, out ulong item) && item != 0)
                        {
                            pending.Push((item, false));
                        }
                    }
                    continue;
                }

                renderer.TryReadInstancePointer(entry.Address, data.MethodTable, "_target", out ulong instance, searchParents: true);
                DacCommandHelpers.WriteDacAddressLink(Console, instance, memory.PointerSize, "DumpObj", dml, forceDml: dml);
                Console.Write(" ");
                ClrDataAddress method = 0;
                if (TryGetMethod(renderer, entry.Address, data.MethodTable, "_methodPtrAux", &method) ||
                    TryGetMethod(renderer, entry.Address, data.MethodTable, "_methodPtr", &method))
                {
                    DacCommandHelpers.WriteDacAddressLink(Console, method, memory.PointerSize, "DumpMD", dml, forceDml: dml);
                    Console.Write(" ");
                    fixed (char* buffer = name)
                    {
                        Console.WriteLine(Dac.GetMethodDescName(method, (uint)name.Length, buffer, null) == 0
                            ? DacCommandHelpers.ReadNullTerminatedString(name) : "UNKNOWN");
                    }
                }
                else
                {
                    Console.WriteLine("(unknown)");
                }
            }
        }

        private unsafe bool TryGetMethod(DacObjectRenderer renderer, ulong address, ulong methodTable, string field, ClrDataAddress* method)
        {
            if (!renderer.TryReadInstancePointer(address, methodTable, field, out ulong ip, searchParents: true) || ip == 0)
            {
                return false;
            }
            ip = ToClrDataAddress(ip, memory.PointerSize);
            if (Dac.GetMethodDescPtrFromIP(ip, method) == 0)
            {
                return true;
            }
            DacpCodeHeaderData header = default;
            if (Dac.GetCodeHeaderData(ip, &header) != 0)
            {
                return false;
            }
            *method = header.MethodDescPtr;
            return true;
        }

        private unsafe bool IsDelegate(ulong methodTable)
        {
            HashSet<ulong> visited = new();
            while (methodTable != 0 && visited.Add(methodTable))
            {
                if (DacCommandHelpers.GetMethodTableName(Dac, methodTable) == "System.Delegate")
                {
                    return true;
                }
                DacpMethodTableData data = default;
                if (Dac.GetMethodTableData(methodTable, &data) != 0)
                {
                    break;
                }
                methodTable = data.parentMethodTable;
            }
            return false;
        }
    }
}
