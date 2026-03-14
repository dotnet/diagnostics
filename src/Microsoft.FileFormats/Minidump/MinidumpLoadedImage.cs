// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.FileFormats.PE;

namespace Microsoft.FileFormats.Minidump
{
    public class MinidumpLoadedImage
    {
        private readonly MinidumpModule _module;
        private readonly Lazy<PEFile> _peFile;
        private readonly Lazy<string> _moduleName;

        /// <summary>
        /// The minidump containing this loaded image.
        /// </summary>
        public Minidump Minidump { get; private set; }

        /// <summary>
        /// The base address in the minidump's virtual address space that this image is mapped.
        /// </summary>
        public ulong BaseAddress { get { return _module.Baseofimage; } }

        /// <summary>
        /// The checksum of this image.
        /// </summary>
        public uint CheckSum { get { return _module.CheckSum; } }

        /// <summary>
        /// The TimeDateStamp of this image, as baked into the PE header.  This value is used
        /// for symbol sever requests to obtain a PE image.
        /// </summary>
        public uint TimeDateStamp { get { return _module.TimeDateStamp; } }

        /// <summary>
        /// The compile time size of this PE image as it is baked into the PE header.  This
        /// value is used for simple server requests to obtain a PE image.
        /// </summary>
        public uint ImageSize { get { return _module.SizeOfImage; } }


        /// <summary>
        /// The full name of this module (including path it was originally loaded from on disk).
        /// </summary>
        public string ModuleName { get { return _moduleName.Value; } }

        /// <summary>
        /// A PEFile representing this image.
        /// </summary>
        public PEFile Image { get { return _peFile.Value; } }

        public uint Major { get { return _module.VersionInfo.FileVersionMS >> 16; } }
        public uint Minor { get { return _module.VersionInfo.FileVersionMS & 0xffff; } }
        public uint Revision { get { return _module.VersionInfo.FileVersionLS >> 16; } }
        public uint Patch { get { return _module.VersionInfo.FileVersionLS & 0xffff; } }

        internal MinidumpLoadedImage(Minidump minidump, MinidumpModule module)
        {
            Minidump = minidump;
            _module = module;
            _peFile = new Lazy<PEFile>(CreatePEFile);
            _moduleName = new Lazy<string>(GetModuleName);
        }

        private PEFile CreatePEFile()
        {
            return new PEFile(new RelativeAddressSpace(Minidump.VirtualAddressReader.DataSource, BaseAddress, Minidump.VirtualAddressReader.Length), true);
        }

        private string GetModuleName()
        {
            return Minidump.DataSourceReader.ReadCountedString(_module.ModuleNameRva, Encoding.Unicode);
        }
    }
}
