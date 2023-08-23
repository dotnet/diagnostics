// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Provides information about loaded modules in a <see cref="DataTarget"/>.
    /// </summary>
    public abstract class ModuleInfo
    {
        /// <summary>
        /// Attempts to create a <see cref="ModuleInfo"/> object from a data reader and a base address.
        /// This function returns <see langword="null"/> if an image was not found at that address or if
        /// we could not determine the format of that image.
        /// </summary>
        /// <param name="reader">The data reader to create this module from.</param>
        /// <param name="baseAddress">The base address of this module.</param>
        /// <param name="name">The name of the module.</param>
        /// <returns>A constructed ModuleInfo, or null.</returns>
        public static ModuleInfo? TryCreate(IDataReader reader, ulong baseAddress, string name)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));

            name ??= "";

            try
            {
                Span<byte> buffer = stackalloc byte[4];
                if (reader.Read(baseAddress, buffer) != buffer.Length)
                    return null;

                if (Unsafe.As<byte, ushort>(ref buffer[0]) == 0x5a4d)
                    return new PEModuleInfo(reader, baseAddress, name, isVirtualHint: true);

                uint header = Unsafe.As<byte, uint>(ref buffer[0]);
                if (header == ElfHeaderCommon.Magic)
                {
                    ElfFile elf = new(reader, baseAddress);
                    long size = elf.ProgramHeaders.Max(r => (long)r.VirtualAddress + (long)r.VirtualSize);
                    return new ElfModuleInfo(reader, elf, baseAddress, size, name);
                }

                if (header == MacOS.MachHeader64.Magic64)
                {
                    MacOS.MachOModule module = new(reader, baseAddress, name);
                    return new MacOS.MachOModuleInfo(module, baseAddress, name, null, module.ImageSize);
                }
            }
            catch
            {
                // We could encounter any number of errors in the Elf/Mach-O system, we will just ignore them here.
            }

            return null;
        }

        /// <summary>
        /// Attempts to create a <see cref="ModuleInfo"/> object from a data reader and a base address.
        /// This function returns <see langword="null"/> if an image was not found at that address or if
        /// we could not determine the format of that image.  This overload allows manually setting the
        /// file size and timestamp.
        /// </summary>
        /// <param name="reader">The data reader to create this module from.</param>
        /// <param name="baseAddress">The base address of this module.</param>
        /// <param name="name">The name of the module.</param>
        /// <param name="indexFileSize">The file size of this module.</param>
        /// <param name="indexTimeStamp">The timestamp of this module (for PE Images).</param>
        /// <param name="version">The version of the module.</param>
        /// <returns>A constructed ModuleInfo, or null.</returns>
        public static ModuleInfo? TryCreate(IDataReader reader, ulong baseAddress, string name, int indexFileSize, int indexTimeStamp, Version? version)
        {
            ModuleInfo? result = TryCreate(reader, baseAddress, name);
            result?.TrySetProperties(indexFileSize, indexTimeStamp, version);
            return result;
        }

        protected virtual void TrySetProperties(int indexFileSize, int indexTimeStamp, Version? version) { }

        /// <summary>
        /// Returns the kind of module this is.
        /// </summary>
        public abstract ModuleKind Kind { get; }

        /// <summary>
        /// Gets the base address of the this image.
        /// </summary>
        public ulong ImageBase { get; }

        /// <summary>
        /// Retrieves the FileName of this loaded module.  May be empty if it is unknown.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// The size of this image (may be different from <see cref="IndexFileSize"/>).
        /// </summary>
        public virtual long ImageSize => IndexFileSize;

        /// <summary>
        /// Gets the specific file size of the image used to index it on the symbol server.
        /// </summary>
        public virtual int IndexFileSize => 0;

        /// <summary>
        /// Gets the timestamp of the image used to index it on the symbol server.
        /// </summary>
        public virtual int IndexTimeStamp => 0;

        /// <summary>
        /// The version of this module.
        /// </summary>
        public virtual Version Version => new();

        /// <summary>
        /// Gets the Linux BuildId or Mach-O UUID of this module.
        /// </summary>
        public virtual ImmutableArray<byte> BuildId => ImmutableArray<byte>.Empty;

        /// <summary>
        /// Gets the PDB associated with this module.
        /// </summary>
        public virtual PdbInfo? Pdb => null;

        /// <summary>
        /// Gets a value indicating whether the module is managed.
        /// </summary>
        public virtual bool IsManaged => false;

        public virtual ulong GetExportSymbolAddress(string symbol) => 0;

        /// <summary>
        /// The root of the resource tree for this module if one exists, null otherwise.
        /// </summary>
        public virtual IResourceNode? ResourceRoot => null;

        public override string ToString() => FileName;

        public ModuleInfo(ulong imageBase, string fileName)
        {
            if (fileName is null)
                throw new ArgumentNullException(nameof(fileName));

            ImageBase = imageBase;
            FileName = fileName;
        }
    }
}