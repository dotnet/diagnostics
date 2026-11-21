// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal static class UnixSocketPeer
    {
        private const int ErrorInterrupted = 4;
        private const int LinuxSocketLevel = 1;
        private const int MacSocketLevel = 0;
        private const int MacPeerPidOption = 2;

        internal static bool IsConnectedToProcess(Socket socket, int pid)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LinuxPeerCredentials credentials;
                uint length = (uint)Marshal.SizeOf<LinuxPeerCredentials>();
                int result;
                do
                {
                    result = GetLinuxPeerCredentials(
                        socket.Handle.ToInt32(),
                        LinuxSocketLevel,
                        GetLinuxPeerCredentialsOption(RuntimeInformation.ProcessArchitecture),
                        out credentials,
                        ref length);
                }
                while (result != 0 && Marshal.GetLastWin32Error() == ErrorInterrupted);

                if (result != 0)
                {
                    throw CreateException("Unable to inspect diagnostic socket peer");
                }
                if (length != (uint)Marshal.SizeOf<LinuxPeerCredentials>())
                {
                    throw new IOException($"Incomplete diagnostic socket peer credentials: expected {Marshal.SizeOf<LinuxPeerCredentials>()} bytes, received {length}.");
                }

                return credentials.Pid == pid;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                int peerPid;
                uint peerPidLength = sizeof(int);
                int result;
                do
                {
                    result = GetMacPeerPid(
                        socket.Handle.ToInt32(),
                        MacSocketLevel,
                        MacPeerPidOption,
                        out peerPid,
                        ref peerPidLength);
                }
                while (result != 0 && Marshal.GetLastWin32Error() == ErrorInterrupted);

                if (result != 0)
                {
                    throw CreateException("Unable to inspect diagnostic socket peer PID");
                }
                if (peerPidLength != (uint)sizeof(int))
                {
                    throw new IOException($"Incomplete diagnostic socket peer PID: expected {sizeof(int)} bytes, received {peerPidLength}.");
                }

                return peerPid == pid;
            }

            throw new PlatformNotSupportedException("Unix diagnostic socket peer validation is supported only on Linux and macOS.");
        }

        internal static int GetLinuxPeerCredentialsOption(Architecture architecture)
        {
            // SO_PEERCRED is 21 on PowerPC and 17 on the other Linux architectures
            // supported by .NET. Numeric values are used because netstandard2.0's
            // Architecture enum does not include the newer architecture members.
            return (int)architecture switch
            {
                0 or 1 or 2 or 3 or 5 or 6 or 7 or 9 => 17,
                8 => 21,
                _ => throw new PlatformNotSupportedException(
                    $"Linux diagnostic socket peer validation is not supported on {architecture}.")
            };
        }

        private static IOException CreateException(string message)
        {
            Win32Exception error = new(Marshal.GetLastWin32Error());
            return new IOException($"{message}: {error.Message}", error);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LinuxPeerCredentials
        {
            public int Pid;
            public uint Uid;
            public uint Gid;
        }

#pragma warning disable SYSLIB1054
        [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
        private static extern int GetLinuxPeerCredentials(int socket, int level, int optionName, out LinuxPeerCredentials credentials, ref uint length);

        [DllImport("libSystem.B.dylib", EntryPoint = "getsockopt", SetLastError = true)]
        private static extern int GetMacPeerPid(int socket, int level, int optionName, out int pid, ref uint length);
#pragma warning restore SYSLIB1054
    }
}
