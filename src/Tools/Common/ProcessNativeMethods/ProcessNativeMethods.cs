// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Internal.Common.Utils
{
    //This code was copied from:
    //https://github.com/projectkudu/kudu/blob/787c893a9336beb498252bb2f90a06a95763f9e9/Kudu.Core/Infrastructure/ProcessExtensions.cs
    internal static partial class ProcessNativeMethods
    {
        public const int ProcessBasicInformation = 0;
        public const int ProcessWow64Information = 26;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            IntPtr dwSize,
            ref IntPtr lpNumberOfBytesRead);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            out IntPtr lpPtr,
            IntPtr dwSize,
            ref IntPtr lpNumberOfBytesRead);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            ref UNICODE_STRING lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            ref UNICODE_STRING_32 lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool OpenProcessToken(
            IntPtr hProcess,
            uint dwDesiredAccess,
            out IntPtr processToken);

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING_32
        {
            public ushort Length;
            public ushort MaximumLength;
            public int Buffer;
        }

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            // These members must match PROCESS_BASIC_INFORMATION
            internal IntPtr Reserved1;
            internal IntPtr PebBaseAddress;
            internal IntPtr Reserved2_0;
            internal IntPtr Reserved2_1;
            internal IntPtr UniqueProcessId;
            internal IntPtr InheritedFromUniqueProcessId;
        }

        [LibraryImport("ntdll.dll")]
        public static partial int NtQueryInformationProcess(
                IntPtr processHandle,
                int processInformationClass,
                ref ProcessInformation processInformation,
                int processInformationLength,
                out int returnLength);

        [LibraryImport("ntdll.dll", SetLastError = true)]
        public static partial int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref IntPtr processInformation,
            int processInformationLength,
            ref int returnLength);
    }
}
