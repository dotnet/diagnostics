// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpmt", Help = "Displays information about a method table at the specified address.")]
    public sealed class DumpMtCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target, IRuntime runtimeInfo) : SosDacCommandBase(services, runtime)
    {
        [Argument<string>(Name = "address", Help = "Method table address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "-MD", Aliases = new[] { "-md" }, Help = "Display the method table slots.")]
        public bool Methods { get; set; }

        [Option<bool>(Name = "-all", Help = "Display method slots and class details.")]
        public bool All { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            if (WarnIfMinidumpSafeMode())
            {
                return;
            }
            if (Address is null)
            {
                throw new DiagnosticsException("Missing MethodTable address");
            }
            if (!TryParseAddressOrExpression(Address, out ulong address))
            {
                throw new DiagnosticsException("Invalid MethodTable address");
            }
            ulong methodTable = ToClrDataAddress(address & ~3UL, memory.PointerSize);
            DacpMethodTableData type = default;
            if (Dac.GetMethodTableData(methodTable, &type) != 0)
            {
                throw new DiagnosticsException($"{DacCommandHelpers.FormatDacAddress(address, memory.PointerSize)} is not a MethodTable");
            }
            if (type.bIsFree != 0)
            {
                throw new DiagnosticsException("Free MethodTable");
            }

            bool dml = UseDml(target.Host.HostType, DmlEnabled);
            bool upperCase = target.OperatingSystem == OSPlatform.Windows;
            DacObjectRenderer renderer = new(Dac, Console, memory, modules, target.OperatingSystem, dml);
            renderer.WriteAddress($"{"Parent:",-20} ", type.parentMethodTable, "DumpMT");
            ClrDataAddress canonical = 0;
            if (Dac.GetMethodTableForEEClass(type.klass, &canonical) >= 0 && canonical == type.klass)
            {
                if (canonical != 0 && canonical != methodTable)
                {
                    renderer.WriteAddress($"{"Canonical:",-20} ", canonical, "DumpMT");
                }
            }
            else
            {
                WritePointerRow("EEClass:", type.klass, "DumpClass", dml, upperCase);
            }
            WritePointerRow("Module:", type.module, "DumpModule", dml, upperCase);
            WriteRow("Name:", DacCommandHelpers.GetMethodTableName(Dac, methodTable) ?? "<error>");
            WriteRow("mdToken:", DacCommandHelpers.FormatDacAddress(type.cl, memory.PointerSize, upperCase: target.OperatingSystem == OSPlatform.Windows));
            WriteRow("File:", DacCommandHelpers.GetModuleName(Dac, type.module, modules, target.OperatingSystem, memory.PointerSize) ?? "Unknown Module");

            DacpMethodTableCollectibleData collectible = default;
            if (Dac is ISOSDacInterface6 dac6 && dac6.GetMethodTableCollectibleData(methodTable, &collectible) >= 0 &&
                collectible.LoaderAllocatorObjectHandle != 0 &&
                memory.ReadPointer(ToTargetAddress(collectible.LoaderAllocatorObjectHandle, memory.PointerSize), out ulong allocator))
            {
                WritePointerRow("LoaderAllocator:", allocator, "DumpObj", dml, upperCase);
            }
            ClrDataAddress context = 0;
            if (Dac is ISOSDacInterface8 dac8 && dac8.GetAssemblyLoadContext(methodTable, &context) >= 0)
            {
                if (context == 0)
                {
                    WriteRow("AssemblyLoadContext:", "Default ALC - The managed instance of this context doesn't exist yet.");
                }
                else
                {
                    WritePointerRow("AssemblyLoadContext:", context, "DumpObj", dml, upperCase);
                }
            }

            WriteRow("BaseSize:", $"0x{type.baseSize:x}");
            if (type.componentSize != 0)
            {
                WriteRow("ComponentSize:", $"0x{type.componentSize:x}");
            }
            WriteRow("Has GC Pointers:", type.bContainsGCPointers != 0 ? "true" : "false");
            WriteRow("Number of Methods:", $"{type.wNumMethods}");
            Console.WriteLine($"{"Number of IFaces in IFaceMap:",-29} {type.wNumInterfaces}");
            if (Methods || All)
            {
                WriteMethods(methodTable, type.wNumMethods, renderer, dml);
            }
            if (All)
            {
                Console.WriteLine("--------------------------------------");
                Console.WriteLine("Additional Details");
                DumpClassCommand.WriteClassDetails(Dac, Console, renderer, methodTable, type);
            }
        }

        private void WriteRow(string label, string value) => Console.WriteLine($"{label,-20} {value}");

        private void WritePointerRow(string label, ulong address, string command, bool dml, bool upperCase)
        {
            Console.Write($"{label,-20} ");
            WritePointer(address, command, dml, upperCase);
            Console.WriteLine();
        }

        private void WritePointer(ulong address, string command, bool dml, bool upperCase)
        {
            string value = DacCommandHelpers.FormatDacAddress(address, memory.PointerSize, upperCase && !dml);
            Console.WriteDmlExec(value, dml && address != 0 ? $"!{command} /d {value}" : null, forceDml: dml);
        }

        private unsafe void WriteMethods(ulong methodTable, uint methodCount, DacObjectRenderer renderer, bool dml)
        {
            int width = memory.PointerSize * 2;
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("MethodDesc Table");
            bool upperCase = target.OperatingSystem == OSPlatform.Windows;
            Console.WriteLine($"{"Entry".PadLeft(width)} {"MethodDesc".PadLeft(width)} {"JIT",6} {"Slot".PadRight(width)} Name");
            ISOSMethodEnum enumerator = null;
            if (Dac is ISOSDacInterface15 dac15)
            {
                DacComNullableByRef<ISOSMethodEnum> output = new(isNullRef: false);
                if (dac15.GetMethodTableSlotEnumerator(methodTable, output) < 0 || output.Interface is null)
                {
                    throw new DiagnosticsException("Unable to enumerate method table slots");
                }
                enumerator = output.Interface;
            }
            Span<char> name = stackalloc char[2048];
            SOSMethodData[] entries = enumerator is null ? null : new SOSMethodData[1];
            for (uint slot = 0; ; slot++)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                SOSMethodData entry = default;
                if (enumerator is null)
                {
                    // Older DACs expose individual slots but not the enumerator.
                    if (slot >= methodCount)
                    {
                        break;
                    }
                    entry.Slot = slot;
                    if (Dac.GetMethodTableSlot(methodTable, slot, &entry.Entrypoint) != 0)
                    {
                        Console.WriteLine($"<error getting slot {slot}>");
                        continue;
                    }
                }
                else
                {
                    uint fetched = 0;
                    if (enumerator.Next(1, entries, &fetched) < 0)
                    {
                        throw new DiagnosticsException("Unable to read the next method table slot");
                    }
                    if (fetched == 0)
                    {
                        break;
                    }
                }
                if (entries is not null)
                {
                    entry = entries[0];
                }
                DacpMethodDescData method = default;
                DacpCodeHeaderData header = default;
                JitTypes jitType = JitTypes.TYPE_UNKNOWN;
                if (entry.Entrypoint != 0)
                {
                    ulong fromIp = 0;
                    if (Dac.GetCodeHeaderData(entry.Entrypoint, &header) == 0)
                    {
                        jitType = header.JITType;
                        fromIp = header.MethodDescPtr;
                    }
                    if (enumerator is null)
                    {
                        entry.MethodDesc = fromIp;
                    }
                    else if (entry.MethodDesc != 0 && fromIp != entry.MethodDesc)
                    {
                        Console.WriteLine("MethodDesc from IP2MD does not match MethodDesc from enumerator");
                    }
                }
                string jit = "NONE";
                bool hasMethod = entry.MethodDesc != 0 && Dac.GetMethodDescData(entry.MethodDesc, 0, &method, 0, null, null) == 0;
                if (hasMethod && jitType == JitTypes.TYPE_UNKNOWN && Dac.GetCodeHeaderData(method.NativeCodeAddr, &header) == 0)
                {
                    jitType = header.JITType;
                }
                if (jitType != JitTypes.TYPE_UNKNOWN)
                {
                    jit = jitType == JitTypes.TYPE_JIT ? "JIT" : jitType == JitTypes.TYPE_PJIT ? "PreJIT" : "NONE";
                }
                else if (hasMethod && runtimeInfo.RuntimeModule is IModule module &&
                    method.NativeCodeAddr >= module.ImageBase && method.NativeCodeAddr - module.ImageBase < module.ImageSize)
                {
                    jit = "FCALL";
                }

                Console.Write($"{DacCommandHelpers.FormatDacAddress(entry.Entrypoint, memory.PointerSize, upperCase)} ");
                WritePointer(entry.MethodDesc, "DumpMD", dml, upperCase);
                Console.Write($" {jit,6} {DacCommandHelpers.FormatDacAddress(entry.Slot, memory.PointerSize, upperCase)} ");
                string methodName;
                if (entry.MethodDesc != 0)
                {
                    fixed (char* buffer = name)
                    {
                        methodName = Dac.GetMethodDescName(entry.MethodDesc, (uint)name.Length, buffer, null) == 0
                            ? DacCommandHelpers.ReadNullTerminatedString(name) : "UNKNOWN";
                    }
                }
                else
                {
                    using MetadataReaderProvider metadata = renderer.OpenMetadata(entry.DefiningModule);
                    MetadataReader reader = metadata?.GetMetadataReader();
                    int row = (int)(entry.Token & 0x00ffffff);
                    methodName = reader is not null && (entry.Token >> 24) == 6 && row > 0 && row <= reader.MethodDefinitions.Count
                        ? reader.GetString(reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row)).Name)
                        : $"Unknown Module!{entry.Token:x8}";
                }
                Console.WriteLine(methodName);
            }
        }
    }
}
