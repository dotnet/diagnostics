// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed class LinuxFunctions : CoreFunctions
    {
#if !NETCOREAPP3_1
        private readonly Func<string, int, IntPtr> _dlopen;
        private readonly Func<IntPtr> _dlerror;
        private readonly Func<IntPtr, int> _dlclose;
        private readonly Func<IntPtr, string, IntPtr> _dlsym;

        public LinuxFunctions()
        {
            // On glibc based Linux distributions, 'libdl.so' is a symlink provided by development packages.
            // To work on production machines, we fall back to 'libdl.so.2' which is the actual library name.
            bool useGlibcDl = false;
            try
            {
                NativeMethods.dlopen("/", 0);
            }
            catch (DllNotFoundException)
            {
                try
                {
                    NativeMethods.dlopen_glibc("/", 0);
                    useGlibcDl = true;
                }
                catch (DllNotFoundException)
                {
                }
            }

            if (useGlibcDl)
            {
                _dlopen = NativeMethods.dlopen_glibc;
                _dlerror = NativeMethods.dlerror_glibc;
                _dlclose = NativeMethods.dlclose_glibc;
                _dlsym = NativeMethods.dlsym_glibc;
            }
            else
            {
                _dlopen = NativeMethods.dlopen;
                _dlerror = NativeMethods.dlerror;
                _dlclose = NativeMethods.dlclose;
                _dlsym = NativeMethods.dlsym;
            }
        }
#endif

        internal override unsafe bool GetFileVersion(string dll, out int major, out int minor, out int revision, out int patch)
        {
            using FileStream stream = File.OpenRead(dll);
            StreamAddressSpace streamAddressSpace = new(stream);
            Reader streamReader = new(streamAddressSpace);
            using ElfFile file = new(streamReader);
            IElfHeader header = file.Header;

            ElfSectionHeader headerStringHeader = new(streamReader, header.Is64Bit, header.SectionHeaderOffset + (ulong)header.SectionHeaderStringIndex * header.SectionHeaderEntrySize);
            ulong headerStringOffset = headerStringHeader.FileOffset;

            ulong dataOffset = 0;
            ulong dataSize = 0;
            for (uint i = 0; i < header.SectionHeaderCount; i++)
            {
                if (i == header.SectionHeaderStringIndex)
                {
                    continue;
                }

                ElfSectionHeader sectionHeader = new(streamReader, header.Is64Bit, header.SectionHeaderOffset + i * header.SectionHeaderEntrySize);
                if (sectionHeader.Type == ElfSectionHeaderType.ProgBits)
                {
                    string sectionName = streamReader.ReadNullTerminatedAscii(headerStringOffset + sectionHeader.NameIndex * sizeof(byte));
                    if (sectionName == ".data")
                    {
                        dataOffset = sectionHeader.FileOffset;
                        dataSize = sectionHeader.FileSize;
                        break;
                    }
                }
            }

            DebugOnly.Assert(dataOffset != 0);
            DebugOnly.Assert(dataSize != 0);

            Span<byte> buffer = stackalloc byte[s_versionLength];
            ulong address = dataOffset;
            ulong endAddress = address + dataSize;

            Span<byte> bytes = stackalloc byte[1];
            Span<char> chars = stackalloc char[1];

            while (address < endAddress)
            {
                int read = streamAddressSpace.Read(address, buffer);
                if (read < s_versionLength)
                {
                    break;
                }

                if (!buffer.SequenceEqual(s_versionString))
                {
                    address++;
                    continue;
                }

                address += (uint)s_versionLength;

                // TODO:  This should be cleaned up to not read byte by byte in the future.  Leaving it here
                // until we decide whether to rewrite the Linux coredumpreader or not.
                StringBuilder builder = new();
                while (address < endAddress)
                {
                    read = streamAddressSpace.Read(address, bytes);
                    if (read < bytes.Length)
                    {
                        break;
                    }

                    if (bytes[0] == '\0')
                    {
                        break;
                    }

                    if (bytes[0] == ' ')
                    {
                        try
                        {
                            System.Version v = System.Version.Parse(builder.ToString());
                            major = v.Major;
                            minor = v.Minor;
                            revision = v.Build;
                            patch = v.Revision;
                            return true;
                        }
                        catch (FormatException)
                        {
                            break;
                        }
                    }

                    fixed (byte* bytesPtr = &MemoryMarshal.GetReference(bytes))
                    fixed (char* charsPtr = &MemoryMarshal.GetReference(chars))
                    {
                        _ = Encoding.ASCII.GetChars(bytesPtr, bytes.Length, charsPtr, chars.Length);
                    }

                    _ = builder.Append(chars[0]);
                    address++;
                }

                break;
            }

            major = minor = revision = patch = 0;
            return false;
        }

        public override bool TryGetWow64(IntPtr proc, out bool result)
        {
            result = false;
            return true;
        }

#if !NETCOREAPP3_1
        public override IntPtr LoadLibrary(string libraryPath)
        {
            IntPtr handle = _dlopen(libraryPath, NativeMethods.RTLD_NOW);
            if (handle == IntPtr.Zero)
                throw new DllNotFoundException(Marshal.PtrToStringAnsi(_dlerror()));

            return handle;
        }

        public override bool FreeLibrary(IntPtr handle)
        {
            return _dlclose(handle) == 0;
        }

        public override IntPtr GetLibraryExport(IntPtr handle, string name)
        {
            return _dlsym(handle, name);
        }

        internal static class NativeMethods
        {
            private const string LibDlGlibc = "libdl.so.2";
            private const string LibDl = "libdl.so";

            internal const int RTLD_NOW = 2;

            [DllImport(LibDlGlibc, EntryPoint = nameof(dlopen))]
            internal static extern IntPtr dlopen_glibc(string fileName, int flags);

            [DllImport(LibDlGlibc, EntryPoint = nameof(dlerror))]
            internal static extern IntPtr dlerror_glibc();

            [DllImport(LibDlGlibc, EntryPoint = nameof(dlclose))]
            internal static extern int dlclose_glibc(IntPtr handle);

            [DllImport(LibDlGlibc, EntryPoint = nameof(dlsym))]
            internal static extern IntPtr dlsym_glibc(IntPtr handle, string symbol);

            [DllImport(LibDl)]
            internal static extern IntPtr dlopen(string fileName, int flags);

            [DllImport(LibDl)]
            internal static extern IntPtr dlerror();

            [DllImport(LibDl)]
            internal static extern int dlclose(IntPtr handle);

            [DllImport(LibDl)]
            internal static extern IntPtr dlsym(IntPtr handle, string symbol);
        }
#endif

        [DllImport("libc")]
        public static extern int symlink(string file, string symlink);
    }
}
