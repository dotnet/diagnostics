// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.MacOS;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed class MacOSFunctions : CoreFunctions
    {
        public override bool TryGetWow64(IntPtr proc, out bool result)
        {
            result = false;
            return true;
        }

        internal override unsafe bool GetFileVersion(string dll, out int major, out int minor, out int revision, out int patch)
        {
            using FileStream stream = File.OpenRead(dll);
            MachOFileReader reader = new(stream);

            MachOHeader64 header = reader.Read<MachOHeader64>();
            if (header.Magic != MachOHeader64.ExpectedMagic)
            {
                throw new NotSupportedException();
            }

            long dataOffset = 0;
            long dataSize = 0;

            byte[] dataSegmentName = Encoding.ASCII.GetBytes("__DATA\0");
            byte[] dataSectionName = Encoding.ASCII.GetBytes("__data\0");
            for (int c = 0; c < header.NumberOfCommands; c++)
            {
                MachOCommand command = reader.Read<MachOCommand>();
                MachOCommandType commandType = command.Command;
                int commandSize = command.CommandSize;

                if (commandType == MachOCommandType.Segment64)
                {
                    long position = stream.Position;
                    MachOSegmentCommand64 segmentCommand = reader.Read<MachOSegmentCommand64>();
                    if (new ReadOnlySpan<byte>(segmentCommand.SegmentName, dataSegmentName.Length).SequenceEqual(dataSegmentName))
                    {
                        for (int s = 0; s < segmentCommand.NumberOfSections; s++)
                        {
                            MachOSection64 section = reader.Read<MachOSection64>();
                            if (new ReadOnlySpan<byte>(section.SectionName, dataSectionName.Length).SequenceEqual(dataSectionName))
                            {
                                dataOffset = section.Offset;
                                dataSize = section.Size;
                                break;
                            }
                        }

                        break;
                    }

                    stream.Position = position;
                }

                stream.Seek(commandSize - sizeof(MachOCommand), SeekOrigin.Current);
            }

            Span<byte> buffer = stackalloc byte[s_versionLength];
            long address = dataOffset;
            long endAddress = address + dataSize;

            Span<byte> bytes = stackalloc byte[1];
            Span<char> chars = stackalloc char[1];

            while (address < endAddress)
            {
                int read = reader.Read(address, buffer);
                if (read < s_versionLength)
                {
                    break;
                }

                if (!buffer.SequenceEqual(s_versionString))
                {
                    address++;
                    continue;
                }

                address += s_versionLength;

                // TODO:  This should be cleaned up to not read byte by byte in the future.  Leaving it here
                // until we decide whether to rewrite the Linux coredumpreader or not.
                StringBuilder builder = new();
                while (address < endAddress)
                {
                    read = reader.Read(address, bytes);
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

#if !NETCOREAPP3_1
        public override bool FreeLibrary(IntPtr handle)
        {
            return NativeMethods.dlclose(handle) == 0;
        }

        public override IntPtr GetLibraryExport(IntPtr handle, string name)
        {
            return NativeMethods.dlsym(handle, name);
        }

        public override IntPtr LoadLibrary(string libraryPath)
        {
            IntPtr handle = NativeMethods.dlopen(libraryPath, NativeMethods.RTLD_NOW);
            if (handle == IntPtr.Zero)
                throw new DllNotFoundException(Marshal.PtrToStringAnsi(NativeMethods.dlerror()));

            return handle;
        }

        internal static class NativeMethods
        {
            private const string LibDl = "libdl.dylib";

            internal const int RTLD_NOW = 2;

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
    }
}