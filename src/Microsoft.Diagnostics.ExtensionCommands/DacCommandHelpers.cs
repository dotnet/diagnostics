// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal static class DacCommandHelpers
    {
        private const int NameCapacity = 2048;

        internal static string FormatDacAddress(ulong address, int pointerSize, bool upperCase = false) => ToTargetAddress(address, pointerSize).ToString(
            pointerSize == 4 ? (upperCase ? "X8" : "x8") : (upperCase ? "X16" : "x16"));

        internal static void WriteDacAddressLink(IConsoleService console, ulong address, int pointerSize, string command, bool dml, bool forceDml = false)
        {
            string value = FormatDacAddress(address, pointerSize);
            console.WriteDmlExec(value, dml && address != 0 ? $"!{command} /d {value}" : null, forceDml);
        }

        internal static unsafe string GetAssemblyName(ISOSDacInterface dac, ulong address)
        {
            Span<char> name = stackalloc char[NameCapacity];
            name.Clear();
            fixed (char* buffer = name)
            {
                return dac.GetAssemblyName(address, NameCapacity, buffer, null) == 0 ? ReadNullTerminatedString(name) : null;
            }
        }

        internal static unsafe string GetAppDomainName(ISOSDacInterface dac, ulong address)
        {
            Span<char> name = stackalloc char[NameCapacity];
            name.Clear();
            fixed (char* buffer = name)
            {
                return dac.GetAppDomainName(address, NameCapacity, buffer, null) == 0 ? ReadNullTerminatedString(name) : null;
            }
        }

        internal static unsafe void WriteAssemblyModules(IConsoleService console, ISOSDacInterface dac, DacpAssemblyData assembly, IModuleService modules, OSPlatform targetOS, int pointerSize, bool dml, bool forDumpDomain)
        {
            if (assembly.AssemblySecDesc != 0)
            {
                WriteLine($"SecurityDescriptor: {FormatDacAddress(assembly.AssemblySecDesc, pointerSize, upperCase: forDumpDomain)}");
            }
            WriteLine("  Module");
            if (assembly.ModuleCount == 0 && !forDumpDomain)
            {
                return;
            }

            ClrDataAddress[] addresses = new ClrDataAddress[assembly.ModuleCount];
            if (dac.GetAssemblyModuleList(assembly.AssemblyPtr, assembly.ModuleCount, addresses, null) != 0)
            {
                if (forDumpDomain)
                {
                    WriteLine("SOS Error: Out of memory");
                    return;
                }
                throw new DiagnosticsException("Unable to get array of Modules");
            }

            foreach (ulong module in addresses)
            {
                console.CancellationToken.ThrowIfCancellationRequested();
                console.Write("  ");
                WriteDacAddressLink(console, module, pointerSize, "DumpModule", dml);
                DacpModuleData data = default;
                string path = dac.GetModuleData(module, &data) == 0
                    ? GetModuleName(dac, data, modules, targetOS, pointerSize) ?? (data.isReflection != 0 ? "Dynamic Module" : "Unknown Module")
                    : "Request module data FAILED";
                WriteLine(pointerSize == 4 ? $"            {path}" : $"    {path}");
            }

            void WriteLine(string text)
            {
                console.WriteLine(text);
                console.CancellationToken.ThrowIfCancellationRequested();
            }
        }

        internal static unsafe string GetModuleName(ISOSDacInterface dac, DacpModuleData module, IModuleService modules, OSPlatform targetOS, int pointerSize)
        {
            if (module.PEAssembly == 0)
            {
                return null;
            }

            const int PathCapacity = 260;
            Span<char> buffer = stackalloc char[PathCapacity];
            buffer.Clear();
            fixed (char* name = buffer)
            {
                ClrDataAddress imageBase = 0;
                if (dac.GetPEFileBase(module.PEAssembly, &imageBase) >= 0)
                {
                    if (dac.GetPEFileName(module.PEAssembly, PathCapacity, name, null) >= 0 && name[0] != '\0')
                    {
                        return ReadNullTerminatedString(buffer);
                    }

                    if (imageBase != 0 && targetOS == OSPlatform.Windows)
                    {
                        try
                        {
                            string path = modules.GetModuleFromBaseAddress(ToTargetAddress(imageBase, pointerSize)).FileName;
                            if (!string.IsNullOrEmpty(path))
                            {
                                return path;
                            }
                        }
                        catch (DiagnosticsException)
                        {
                            // The managed module need not appear in the debugger's native module list.
                        }
                    }
                }

                DacComNullableByRef<IXCLRDataModule> moduleRef = new(isNullRef: false);
                if (dac.GetModule(module.Address, moduleRef) >= 0 && moduleRef.Interface is IXCLRDataModule dataModule)
                {
                    uint needed = 0;
                    name[0] = '\0';
                    if (dataModule.GetFileName(PathCapacity, &needed, name) >= 0 && name[0] != '\0')
                    {
                        return ReadNullTerminatedString(buffer);
                    }
                }
            }
            return null;
        }

        internal static string ReadNullTerminatedString(ReadOnlySpan<char> buffer)
        {
            int terminator = buffer.IndexOf('\0');
            return (terminator >= 0 ? buffer[..terminator] : buffer).ToString();
        }

        internal static unsafe string GetObjectTypeName(ISOSDacInterface dac, ulong address, int pointerSize)
        {
            DacpObjectData data = default;
            if (dac.GetObjectData(ToClrDataAddress(address, pointerSize), &data) < 0)
            {
                return "<error>";
            }
            if (data.ObjectType == DacpObjectType.OBJ_FREE)
            {
                return "Free";
            }

            ulong methodTable = data.MethodTable & ~(ulong)(pointerSize - 1);
            DacpUsefulGlobalsData globals = default;
            bool array = methodTable != 0 && data.ElementTypeHandle != 0 &&
                dac.GetUsefulGlobals(&globals) >= 0 && methodTable == globals.ArrayMethodTable;
            if (array)
            {
                methodTable = data.ElementTypeHandle;
            }

            uint needed = 0;
            if (dac.GetMethodTableName(methodTable, 0, null, &needed) < 0 || needed == 0)
            {
                return "<error>";
            }

            char[] name = new char[needed];
            fixed (char* buffer = name)
            {
                return dac.GetMethodTableName(methodTable, needed, buffer, null) >= 0
                    ? ReadNullTerminatedString(name) + (array ? "[]" : "")
                    : "<error>";
            }
        }
    }
}
