// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed unsafe class WindowsFunctions : CoreFunctions
    {
        internal static bool IsProcessRunning(int processId)
        {
            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION, false, processId);
            if (handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(handle);
                return true;
            }

            int minimumLength = 256;
            int[] processIds = ArrayPool<int>.Shared.Rent(minimumLength);
            try
            {
                int size;
                for (; ; )
                {
                    NativeMethods.EnumProcesses(processIds, processIds.Length * sizeof(int), out size);
                    if (size == processIds.Length * sizeof(int))
                    {
                        ArrayPool<int>.Shared.Return(processIds);
                        minimumLength *= 2;
                        processIds = ArrayPool<int>.Shared.Rent(minimumLength);
                        continue;
                    }

                    break;
                }

                return Array.IndexOf(processIds, processId, 0, size / sizeof(int)) >= 0;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(processIds);
            }
        }

        internal override bool GetFileVersion(string dll, out int major, out int minor, out int revision, out int patch)
        {
            major = minor = revision = patch = 0;

            int len = NativeMethods.GetFileVersionInfoSize(dll, out int handle);
            if (len <= 0)
                return false;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                fixed (byte* data = buffer)
                {
                    if (!NativeMethods.GetFileVersionInfo(dll, handle, len, data))
                        return false;

                    if (!NativeMethods.VerQueryValue(data, "\\", out IntPtr ptr, out len))
                        return false;

                    DebugOnly.Assert(unchecked((int)ptr.ToInt64()) % sizeof(ushort) == 0);

                    minor = Unsafe.Read<ushort>((ptr + 8).ToPointer());
                    major = Unsafe.Read<ushort>((ptr + 10).ToPointer());
                    patch = Unsafe.Read<ushort>((ptr + 12).ToPointer());
                    revision = Unsafe.Read<ushort>((ptr + 14).ToPointer());

                    return true;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

#if !NETCOREAPP3_1
        public override IntPtr LoadLibrary(string libraryPath)
        {
            if (libraryPath is null)
                throw new ArgumentNullException(nameof(libraryPath));

            IntPtr handle = NativeMethods.LoadLibrary(libraryPath);
            if (handle == IntPtr.Zero)
                if (Marshal.GetLastWin32Error() == 193)
                    throw new BadImageFormatException(); // ERROR_BAD_EXE_FORMAT
                else
                    throw new DllNotFoundException();

            return handle;
        }

        public override bool FreeLibrary(IntPtr handle)
        {
            return NativeMethods.FreeLibrary(handle);
        }

        public override IntPtr GetLibraryExport(IntPtr handle, string name)
        {
            return NativeMethods.GetProcAddress(handle, name);
        }
#endif

        internal static class NativeMethods
        {
            private const string Kernel32LibraryName = "kernel32.dll";
            private const string VersionLibraryName = "version.dll";

            public const int PROCESS_QUERY_INFORMATION = 0x0400;

            [DllImport(Kernel32LibraryName, SetLastError = true)]
            public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

            [DllImport(Kernel32LibraryName, SetLastError = true)]
            public static extern bool CloseHandle(IntPtr hObject);

            [DllImport(Kernel32LibraryName, SetLastError = true, EntryPoint = "K32EnumProcesses")]
            public static extern unsafe bool EnumProcesses(int[] lpidProcess, int cb, out int lpcbNeeded);

            [DllImport(Kernel32LibraryName)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FreeLibrary(IntPtr hModule);

            [DllImport(Kernel32LibraryName, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryW")]
            public static extern IntPtr LoadLibrary(string lpLibFileName);

            [DllImport(Kernel32LibraryName)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWow64Process(IntPtr hProcess, out bool isWow64);

            [DllImport(VersionLibraryName, CharSet = CharSet.Unicode, EntryPoint = "GetFileVersionInfoW")]
            public static extern bool GetFileVersionInfo(string sFileName, int handle, int size, byte* infoBuffer);

            [DllImport(VersionLibraryName, CharSet = CharSet.Unicode, EntryPoint = "GetFileVersionInfoSizeW")]
            public static extern int GetFileVersionInfoSize(string sFileName, out int handle);

            [DllImport(VersionLibraryName, CharSet = CharSet.Unicode, EntryPoint = "VerQueryValueW")]
            public static extern bool VerQueryValue(byte* pBlock, string pSubBlock, out IntPtr val, out int len);

            [DllImport(Kernel32LibraryName)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);
        }

        public override bool TryGetWow64(IntPtr proc, out bool result)
        {
            if (Environment.OSVersion.Version.Major > 5 ||
                Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1)
            {
                return NativeMethods.IsWow64Process(proc, out result);
            }

            result = false;
            return false;
        }
    }
}