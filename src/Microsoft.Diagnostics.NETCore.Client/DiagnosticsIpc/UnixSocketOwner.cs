// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal static class UnixSocketOwner
    {
        internal static uint GetProcessUid(int pid)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                foreach (string line in File.ReadLines($"/proc/{pid}/status"))
                {
                    if (line.StartsWith("Uid:", StringComparison.Ordinal))
                    {
                        return ParseLinuxUid(line);
                    }
                }
                throw new IOException($"Unable to read the filesystem UID of process {pid}.");
            }

            const int ProcPidTBsdInfo = 3;
            int size = Marshal.SizeOf<MacProcessInfo>();
            int bytesRead = ProcPidInfo(pid, ProcPidTBsdInfo, 0, out MacProcessInfo info, size);
            if (bytesRead == 0)
            {
                throw CreateException($"Unable to read the effective UID of process {pid}");
            }
            if (bytesRead != size)
            {
                throw new IOException($"Incomplete process information for process {pid}: expected {size} bytes, received {bytesRead}.");
            }
            return info.Uid;
        }

        internal static uint ParseLinuxUid(string line)
        {
            string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // /proc status reports real, effective, saved, and filesystem UIDs in the reader's user namespace.
            if (fields.Length != 5 || fields[0] != "Uid:" ||
                !uint.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out uint uid))
            {
                throw new IOException("Invalid Uid field in process status.");
            }
            return uid;
        }

        internal static bool IsSocketOwnedBy(string path, uint uid)
        {
            int result;
            uint mode;
            uint owner;
            do
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    int syscall;
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X64: syscall = 262; break;
                        case Architecture.Arm64: syscall = 79; break;
                        case Architecture.Arm: syscall = 327; break;
                        case Architecture.X86: syscall = 300; break;
                        default:
                            throw new PlatformNotSupportedException($"Socket ownership checks are not supported on {RuntimeInformation.ProcessArchitecture}.");
                    }

                    // fstatat(AT_FDCWD, path, stat, AT_SYMLINK_NOFOLLOW) through the stable kernel ABI.
                    // This avoids libc-specific stat layouts and newer glibc exports on both glibc and musl.
                    result = FStatAt(new IntPtr(syscall), new IntPtr(-100), path, out LinuxStat stat, new IntPtr(0x100)).ToInt32();
                    bool x64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
                    mode = x64 ? stat.X64Mode : stat.Mode;
                    owner = x64 ? stat.X64Uid : stat.Uid;
                }
                else
                {
                    MacStat stat;
                    result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                        ? LStatInode64(path, out stat)
                        : LStat(path, out stat);
                    mode = stat.Mode;
                    owner = stat.Uid;
                }
            }
            while (result != 0 && Marshal.GetLastWin32Error() == 4); // EINTR

            if (result != 0)
            {
                // A candidate can disappear between directory enumeration and lstat.
                if (Marshal.GetLastWin32Error() == 2)
                {
                    return false;
                }
                throw CreateException($"Unable to inspect diagnostic socket '{path}'");
            }

            const int FileTypeMask = 0xf000;
            const int SocketType = 0xc000;
            return (mode & FileTypeMask) == SocketType && owner == uid;
        }

        private static IOException CreateException(string message)
        {
            Win32Exception error = new(Marshal.GetLastWin32Error());
            return new IOException($"{message}: {error.Message}", error);
        }

        // Union of Linux UAPI stat (x64/arm64) and stat64 (x86/arm), not libc's struct stat.
        // x64 is the largest (144 bytes); only mode and UID are consumed.
        [StructLayout(LayoutKind.Explicit, Size = 144)]
        private struct LinuxStat
        {
            [FieldOffset(24)] public uint X64Mode;
            [FieldOffset(28)] public uint X64Uid;
            [FieldOffset(16)] public uint Mode;
            [FieldOffset(24)] public uint Uid;
        }

        // Darwin's 64-bit inode struct stat (<sys/stat.h>).
        [StructLayout(LayoutKind.Explicit, Size = 144)]
        private struct MacStat
        {
            [FieldOffset(4)] public ushort Mode;
            [FieldOffset(16)] public uint Uid;
        }

        // Darwin's proc_bsdinfo (<sys/proc_info.h>).
        [StructLayout(LayoutKind.Explicit, Size = 136)]
        private struct MacProcessInfo
        {
            [FieldOffset(20)] public uint Uid;
        }

#pragma warning disable SYSLIB1054
        [DllImport("libc", EntryPoint = "syscall", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr FStatAt(IntPtr number, IntPtr directoryFd, string path, out LinuxStat stat, IntPtr flags);

        [DllImport("libSystem.B.dylib", EntryPoint = "lstat", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int LStat(string path, out MacStat stat);

        [DllImport("libSystem.B.dylib", EntryPoint = "lstat$INODE64", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int LStatInode64(string path, out MacStat stat);

        [DllImport("libproc.dylib", EntryPoint = "proc_pidinfo", SetLastError = true)]
        private static extern int ProcPidInfo(int pid, int flavor, ulong arg, out MacProcessInfo info, int size);
#pragma warning restore SYSLIB1054
    }
}
