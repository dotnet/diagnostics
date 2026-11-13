// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;

namespace Microsoft.Diagnostics.TestHelpers
{
    public static class SingleFileRuntimeValidator
    {
        public static void Validate(string debuggeePath, string dacPath, string dbiPath)
        {
            using Stream stream = File.OpenRead(debuggeePath);
            StreamAddressSpace addressSpace = new(stream);
            using PEFile pe = new(addressSpace);
            using ELFFile elf = new(addressSpace);
            using MachOFile mach = new(addressSpace);

            Reader reader;
            ulong address;
            bool windows = pe.IsValid();
            bool linux = !windows && elf.IsValid();
            if (windows && pe.TryGetExportSymbol(RuntimeInfo.RUNTIME_INFO_SYMBOL, out address))
            {
                reader = pe.RelativeVirtualAddressReader;
            }
            else if (linux && TryGetELFSymbol(elf, RuntimeInfo.RUNTIME_INFO_SYMBOL, out address))
            {
                reader = elf.VirtualAddressReader;
            }
            else if (!windows && !linux && mach.IsValid() &&
                mach.Symtab.TryLookupSymbol(RuntimeInfo.RUNTIME_INFO_SYMBOL, out address))
            {
                reader = mach.VirtualAddressReader;
            }
            else
            {
                throw new InvalidDataException($"Single-file debuggee '{debuggeePath}' has no {RuntimeInfo.RUNTIME_INFO_SYMBOL} export.");
            }

            RuntimeInfo info = MemoryMarshal.Read<RuntimeInfo>(reader.Read(address, (uint)Marshal.SizeOf<RuntimeInfo>()));
            if (!info.IsValid)
            {
                throw new InvalidDataException($"Single-file debuggee '{debuggeePath}' has an invalid {RuntimeInfo.RUNTIME_INFO_SYMBOL} header.");
            }

            ValidateLibrary(dacPath, windows ? info.DacPEIndex : default, info.DacBuildId);
            ValidateLibrary(dbiPath, windows ? info.DbiPEIndex : default, info.DbiBuildId);

            void ValidateLibrary(string path, (int timeStamp, int fileSize) peIndex, ImmutableArray<byte> buildId)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Single-file debuggee '{debuggeePath}' requires a matching DAC/DBI, but '{path ?? "<missing>"}' was not found.", path);
                }

                using Stream libraryStream = File.OpenRead(path);
                StreamAddressSpace librarySpace = new(libraryStream);
                string expected;
                string actual;
                if (windows)
                {
                    using PEFile library = new(librarySpace);
                    expected = $"{peIndex.timeStamp:X8}/{peIndex.fileSize:X8}";
                    actual = library.IsValid() ? $"{library.Timestamp:X8}/{library.SizeOfImage:X8}" : "<invalid PE>";
                    if (peIndex.timeStamp == 0 || peIndex.fileSize == 0)
                    {
                        throw new InvalidDataException($"Single-file debuggee '{debuggeePath}' has no PE identity for '{path}'.");
                    }
                }
                else
                {
                    expected = Convert.ToHexString(buildId.AsSpan());
                    if (buildId.IsDefaultOrEmpty)
                    {
                        throw new InvalidDataException($"Single-file debuggee '{debuggeePath}' has no build identity for '{path}'.");
                    }
                    if (linux)
                    {
                        using ELFFile library = new(librarySpace);
                        actual = library.IsValid() ? Convert.ToHexString(library.BuildID) : "<invalid ELF>";
                    }
                    else
                    {
                        using MachOFile library = new(librarySpace);
                        actual = library.IsValid() ? Convert.ToHexString(library.Uuid) : "<invalid MachO>";
                    }
                }

                if (expected != actual)
                {
                    throw new InvalidDataException(
                        $"Single-file runtime identity mismatch: debuggee '{debuggeePath}' requires '{path}' identity {expected}, " +
                        $"but the selected binary has identity {actual}. Product-version equality is insufficient; " +
                        "publish with the matching test SDK host pack and use DAC/DBI from the same runtime build.");
                }
            }
        }

        private static bool TryGetELFSymbol(ELFFile elf, string name, out ulong address)
        {
            foreach (ELFSection symbols in elf.Sections.Where(section => section.Header.Type == ELFSectionHeaderType.DynSym))
            {
                ulong entrySize = symbols.Header.EntrySize;
                uint minimumSize = elf.Is64Bit ? 24u : 16u;
                if (entrySize < minimumSize || symbols.Header.Link >= elf.Sections.Length)
                {
                    throw new InvalidDataException("Invalid ELF dynamic symbol table.");
                }

                Reader names = elf.Sections[symbols.Header.Link].Contents;
                for (ulong offset = 0; offset + entrySize <= symbols.Header.FileSize; offset += entrySize)
                {
                    uint nameOffset = symbols.Contents.Read<uint>(offset);
                    if (names.Read<string>(nameOffset) == name)
                    {
                        address = elf.Is64Bit
                            ? symbols.Contents.Read<ulong>(offset + 8)
                            : symbols.Contents.Read<uint>(offset + 4);
                        return address != 0;
                    }
                }
            }
            address = 0;
            return false;
        }
    }
}
