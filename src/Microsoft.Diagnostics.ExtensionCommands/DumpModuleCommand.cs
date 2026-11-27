// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.DebugServices;
using SOS.Hosting;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static Microsoft.Diagnostics.ExtensionCommands.DacCommandHelpers;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpmodule", Aliases = new[] { "DumpModule" }, Help = "Displays information about a managed module.")]
    public sealed class DumpModuleCommand(IServiceProvider services, RuntimeWrapper runtime, IMemoryService memory, IModuleService modules, ITarget target) : SosDacCommandBase(services, runtime)
    {
        private const int NameCapacity = 2048;
        private const int MaxProfilerMethods = 100;
        // Native SOS uses mdTypeDefNil as the token prefix for both tables.
        private const uint TypeTableTokenPrefix = 0x02000000;

        [Argument<string>(Name = "address", Help = "Module address in hexadecimal or a debugger expression.")]
        public string Address { get; set; }

        [Option<bool>(Name = "--method-tables", Aliases = new[] { "-mt" }, Help = "List type definitions and references.")]
        public bool MethodTables { get; set; }

        [Option<bool>(Name = "--profiler-modified", Aliases = new[] { "-prof" }, Help = "List methods with profiler-modified IL.")]
        public bool ProfilerModified { get; set; }

        [Option<bool>(Name = "--dml", Aliases = new[] { "/d" }, Help = "Enable debugger markup links.")]
        public bool DmlEnabled { get; set; }

        public override unsafe void Invoke()
        {
            if (Address == null)
            {
                throw new DiagnosticsException("Usage: DumpModule [-mt] <Module Address>");
            }
            if (!TryParseAddressOrExpression(Address, out ulong address))
            {
                throw new DiagnosticsException($"Invalid Module {Address}");
            }

            address = ToClrDataAddress(address, memory.PointerSize);
            DacpModuleData module = default;
            if (Dac.GetModuleData(address, &module) != 0)
            {
                throw new DiagnosticsException($"Fail to fill Module {FormatPointer(address)}");
            }

            WriteLine($"Name: {GetModuleName(Dac, module, modules, target.OperatingSystem, memory.PointerSize) ?? "Unknown Module"}");
            Write("Attributes:              ");
            if (module.isPEFile != 0)
            {
                Write("PEFile ");
            }
            if (module.isReflection != 0)
            {
                Write("Reflection ");
            }
            if ((module.dwTransientFlags & (uint)DacpModuleData.TransientFlags.IsEditAndContinue) != 0)
            {
                Write("EditAndContinue ");
            }

            DacpGetModuleData moduleData = default;
            DacComNullableByRef<IXCLRDataModule> moduleRef = new(isNullRef: false);
            if (Dac.GetModule(address, moduleRef) >= 0 && moduleRef.Interface is IXCLRDataModule dataModule &&
                dataModule.Request(0xf0000001 /* DACDATAMODULEPRIV_REQUEST_GET_MODULEDATA */, 0, null, (uint)sizeof(DacpGetModuleData), (byte*)&moduleData) >= 0)
            {
                if (moduleData.IsDynamic != 0)
                {
                    Write("IsDynamic ");
                }
                if (moduleData.IsInMemory != 0)
                {
                    Write("IsInMemory ");
                }
                if (moduleData.IsFileLayout != 0)
                {
                    Write("IsFileLayout ");
                }
            }
            WriteLine();

            Write("Assembly:                ");
            WriteDacAddressLink(Console, module.Assembly, memory.PointerSize, "DumpAssembly", DmlEnabled);
            WriteLine();
            WriteLine($"BaseAddress:             {FormatPointer(module.ilBase)}");
            WriteLine($"LoaderHeap:              {FormatPointer(module.LoaderAllocator)}");
            WriteLine($"TypeDefToMethodTableMap: {FormatPointer(module.TypeDefToMethodTableMap)}");
            WriteLine($"TypeRefToMethodTableMap: {FormatPointer(module.TypeRefToMethodTableMap)}");
            WriteLine($"MethodDefToDescMap:      {FormatPointer(module.MethodDefToDescMap)}");
            WriteLine($"FieldDefToDescMap:       {FormatPointer(module.FieldDefToDescMap)}");
            WriteLine($"MemberRefToDescMap:      {FormatPointer(module.MemberRefToDescMap)}");
            WriteLine($"FileReferencesMap:       {FormatPointer(module.FileReferencesMap)}");
            WriteLine($"AssemblyReferencesMap:   {FormatPointer(module.ManifestModuleReferencesMap)}");
            if (module.ilBase != 0 && module.metadataStart != 0)
            {
                WriteLine($"MetaData start address:  {FormatPointer(module.metadataStart)} ({unchecked((int)module.metadataSize)} bytes)");
            }

            if (MethodTables)
            {
                WriteModuleMap(address, isReference: false);
                WriteModuleMap(address, isReference: true);
            }
            if (ProfilerModified)
            {
                WriteProfilerMethods(address);
            }
        }

        private string FormatPointer(ulong address) =>
            FormatDacAddress(address, memory.PointerSize, upperCase: target.OperatingSystem == OSPlatform.Windows);

        private unsafe void WriteModuleMap(ulong module, bool isReference)
        {
            int width = memory.PointerSize * 2;
            WriteLine($"\nTypes {(isReference ? "referenced" : "defined")} in this module\n");
            WriteLine($"{"MT".PadLeft(width)}{(isReference ? "   " : " ")}{(isReference ? "TypeRef" : "TypeDef").PadLeft(width)} Name");
            WriteLine("------------------------------------------------------------------------------");

            ModuleMap map = new(Console.CancellationToken);
            GCHandle handle = GCHandle.Alloc(map);
            try
            {
                Dac.TraverseModuleMap(isReference ? ModuleMapType.TYPEREFTOMETHODTABLE : ModuleMapType.TYPEDEFTOMETHODTABLE, module, &CollectMethodTable, (void*)GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
            if (map.Error != null)
            {
                ExceptionDispatchInfo.Throw(map.Error);
            }

            Console.CancellationToken.ThrowIfCancellationRequested();
            Span<char> name = stackalloc char[NameCapacity];
            fixed (char* buffer = name)
            {
                foreach ((uint index, ulong methodTable) in map.Entries)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    name[0] = '\0';
                    Dac.GetMethodTableName(methodTable, NameCapacity, buffer, null);
                    WriteDacAddressLink(Console, methodTable, memory.PointerSize, "DumpMT", DmlEnabled);
                    WriteLine($" 0x{(index | TypeTableTokenPrefix):x8} {ReadNullTerminatedString(name)}");
                }
            }
        }

        private sealed class ModuleMap(CancellationToken cancellationToken)
        {
            public readonly CancellationToken CancellationToken = cancellationToken;
            public readonly List<(uint Index, ulong MethodTable)> Entries = new();
            public Exception Error;
        }

        [UnmanagedCallersOnly]
        private static unsafe void CollectMethodTable(uint index, ulong methodTable, void* context)
        {
            ModuleMap map = (ModuleMap)GCHandle.FromIntPtr((nint)context).Target;
            if (map.Error != null || map.CancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                map.Entries.Add((index, methodTable));
            }
            catch (Exception ex)
            {
                // A managed exception cannot escape the native traversal callback.
                map.Error = ex;
            }
        }

        private unsafe void WriteProfilerMethods(ulong module)
        {
            ClrDataAddress* methods = stackalloc ClrDataAddress[MaxProfilerMethods];
            int count = 0;
            if (Dac is not ISOSDacInterface7 profiler || profiler.GetMethodsWithProfilerModifiedIL(module, methods, MaxProfilerMethods, &count) < 0)
            {
                WriteLine("\nThis runtime version does not support listing the profiler modified functions.");
                return;
            }
            if (count <= 0)
            {
                WriteLine("\nThis module has no methods with profiler modified IL.");
                return;
            }
            WriteLine("\nMethods in this module with profiler modified IL:");
            Span<char> name = stackalloc char[NameCapacity];
            fixed (char* buffer = name)
            {
                for (int i = 0; i < Math.Min(count, MaxProfilerMethods); i++)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    ulong method = methods[i];
                    Write("MethodDesc: ");
                    WriteDacAddressLink(Console, method, memory.PointerSize, "DumpMD", DmlEnabled);
                    Write(" ");
                    name[0] = '\0';
                    if (Dac.GetMethodDescName(method, NameCapacity, buffer, null) == 0)
                    {
                        Write($"Name: {ReadNullTerminatedString(name)}");
                    }
                    DacpProfilerILData ilData = default;
                    if (profiler.GetProfilerModifiedILInformation(method, &ilData) >= 0)
                    {
                        Write(ilData.type switch
                        {
                            DacpProfilerILData.ModificationType.ILModified => " (IL Modified)",
                            DacpProfilerILData.ModificationType.ReJITModified => " (ReJIT Modified)",
                            _ => "",
                        });
                    }
                    WriteLine();
                }
            }
            if (count >= MaxProfilerMethods)
            {
                WriteLine("Profiler modified methods truncated, reached max value.");
            }
        }
    }
}
