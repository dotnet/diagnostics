// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A representation of an ELF loaded image section.
    /// </summary>
    internal sealed class ElfLoadedImage
    {
        private readonly Reader _vaReader;
        private readonly bool _is64bit;
        private ulong _baseAddress;
        private ulong _minimumPointer = ulong.MaxValue;
        private ulong _end;

        // The path of the image on disk.
        public string FileName { get; }

        /// <summary>
        /// The BaseAddress of this image
        /// </summary>
        public ulong BaseAddress => _baseAddress == 0 ? _minimumPointer : _baseAddress;

        /// <summary>
        /// The size of this image in memory.
        /// </summary>
        public ulong Size => _end - BaseAddress;

        internal ElfLoadedImage(Reader virtualAddressReader, bool is64bit, string path)
        {
            _vaReader = virtualAddressReader;
            _is64bit = is64bit;
            FileName = path;
        }

        /// <summary>
        /// Open the loaded image as an ELFFile.
        /// </summary>
        /// <returns>An ELFFile if this is a valid ELF image, null otherwise.</returns>
        public ElfFile? Open()
        {
            IElfHeader? header;

            if (_is64bit)
                header = _vaReader.TryRead<ElfHeader64>(BaseAddress);
            else
                header = _vaReader.TryRead<ElfHeader32>(BaseAddress);

            if (header is null || !header.IsValid)
                return null;

            return new ElfFile(header, _vaReader, BaseAddress, true);
        }

        /// <summary>
        /// Returns this ELF loaded image as a stream.
        /// </summary>
        /// <returns></returns>
        internal ReaderStream AsStream()
        {
            return new ReaderStream(BaseAddress, Size, _vaReader);
        }

        internal void AddTableEntryPointers(ElfFileTableEntryPointers64 pointers)
        {
            if (_end < pointers.Stop)
                _end = pointers.Stop;

            // There are cases (like .NET single-file modules) where the first NT_FILE entry isn't the ELF
            // or PE header (i.e the base address). The header is the first entry with PageOffset == 0. For
            // ELF modules there should only be one PageOffset == 0 entry but with the memory mapped PE
            // assemblies, there can be more than one PageOffset == 0 entry and the first one is the base
            // address.
            if (_baseAddress == 0 && pointers.PageOffset == 0)
                _baseAddress = pointers.Start;

            // If no load address was found, will use the lowest start address. There has to be at least one
            // entry. This fixes the .NET 5.0 MacOS ELF dumps which have modules with no PageOffset == 0 entries.
            _minimumPointer = Math.Min(pointers.Start, _minimumPointer);
        }

        /// <summary>
        /// Returns <see cref="FileName"/>.
        /// </summary>
        /// <returns><see cref="FileName"/></returns>
        public override string ToString() => FileName;
    }
}
