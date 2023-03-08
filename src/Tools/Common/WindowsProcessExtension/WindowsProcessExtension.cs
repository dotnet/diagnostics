// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Internal.Common.Utils

{
    internal static class WindowsProcessExtension
    {
        //This code was copied from:
        //https://github.com/projectkudu/kudu/blob/787c893a9336beb498252bb2f90a06a95763f9e9/Kudu.Core/Infrastructure/ProcessExtensions.cs
        //The error handling was modified to return a string instead of throw.

        public static string GetCommandLine(Process process)
        {
            IntPtr processHandle;
            try
            {
                processHandle = process.Handle;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return "[cannot determine command line arguments]";
            }

            return GetCommandLineCore(processHandle);
        }

        private static string GetCommandLineCore(IntPtr processHandle)
        {
            int commandLineLength;
            IntPtr commandLineBuffer;
            byte[] commandLine;

            int processBitness = GetProcessBitness(processHandle);

            if (processBitness == 64 && !System.Environment.Is64BitProcess)
            {
                return "[cannot determine command line arguments bitness mismatch]";
            }

            try
            {
                IntPtr pPeb = processBitness == 64 ? GetPeb64(processHandle) : GetPeb32(processHandle);

                int offset = processBitness == 64 ? 0x20 : 0x10;

                int unicodeStringOffset = processBitness == 64 ? 0x70 : 0x40;


                if (!ReadIntPtr(processHandle, pPeb + offset, out IntPtr ptr))
                {
                    return "[cannot determine command line arguments]";
                }

                if ((processBitness == 64 && System.Environment.Is64BitProcess) ||
                    (processBitness == 32 && !System.Environment.Is64BitProcess))
                {
                    //System and Process are both the same bitness.

                    ProcessNativeMethods.UNICODE_STRING unicodeString = default(ProcessNativeMethods.UNICODE_STRING);
                    if (!ProcessNativeMethods.ReadProcessMemory(processHandle, ptr + unicodeStringOffset, ref unicodeString, new IntPtr(Marshal.SizeOf(unicodeString)), IntPtr.Zero))
                    {
                        return "[cannot determine command line arguments]";
                    }

                    commandLineLength = unicodeString.Length;

                    commandLineBuffer = unicodeString.Buffer;
                }

                else
                {
                    //System is 64 bit and the process is 32 bit

                    ProcessNativeMethods.UNICODE_STRING_32 unicodeString32 = default(ProcessNativeMethods.UNICODE_STRING_32);

                    if (!ProcessNativeMethods.ReadProcessMemory(processHandle, ptr + unicodeStringOffset, ref unicodeString32, new IntPtr(Marshal.SizeOf(unicodeString32)), IntPtr.Zero))
                    {
                        return "[cannot determine command line arguments]";
                    }

                    commandLineLength = unicodeString32.Length;
                    commandLineBuffer = new IntPtr(unicodeString32.Buffer);
                }

                commandLine = new byte[commandLineLength];

                if (!ProcessNativeMethods.ReadProcessMemory(processHandle, commandLineBuffer, commandLine, new IntPtr(commandLineLength), IntPtr.Zero))
                {
                    return "[cannot determine command line arguments]";
                }

                return Encoding.Unicode.GetString(commandLine);
            }

            catch (Win32Exception)
            {
                return "[cannot determine command line arguments]";
            }

        }

        private static bool ReadIntPtr(IntPtr hProcess, IntPtr ptr, out IntPtr readPtr)
        {
            IntPtr dataSize = new(IntPtr.Size);
            IntPtr res_len = IntPtr.Zero;
            if (!ProcessNativeMethods.ReadProcessMemory(
                hProcess,
                ptr,
                out readPtr,
                dataSize,
                ref res_len))
            {
                throw new Win32Exception("Reading of the pointer failed. Error: " + Marshal.GetLastWin32Error());
            }

            // This is more like an assert
            return res_len == dataSize;
        }


        private static IntPtr GetPebNative(IntPtr hProcess)
        {
            ProcessNativeMethods.ProcessInformation pbi = default(ProcessNativeMethods.ProcessInformation);
            int pbiSize = Marshal.SizeOf(pbi);
            ProcessNativeMethods.NtQueryInformationProcess(
                hProcess,
                ProcessNativeMethods.ProcessBasicInformation,
                ref pbi,
                pbiSize,
                out int res_len);

            if (res_len != pbiSize)
            {
                throw new Win32Exception("Query Information Process failed. Error: " + Marshal.GetLastWin32Error());
            }

            return pbi.PebBaseAddress;
        }

        private static IntPtr GetPeb64(IntPtr hProcess)
        {
            return GetPebNative(hProcess);
        }

        private static IntPtr GetPeb32(IntPtr hProcess)
        {
            if (System.Environment.Is64BitProcess)
            {
                IntPtr ptr = IntPtr.Zero;
                int res_len = 0;
                int pbiSize = IntPtr.Size;
                ProcessNativeMethods.NtQueryInformationProcess(
                    hProcess,
                    ProcessNativeMethods.ProcessWow64Information,
                    ref ptr,
                    pbiSize,
                    ref res_len);

                if (res_len != pbiSize)
                {
                    throw new Win32Exception("Query Information Process failed. Error: " + Marshal.GetLastWin32Error());
                }

                return ptr;
            }
            else
            {
                return GetPebNative(hProcess);
            }
        }

        private static int GetProcessBitness(IntPtr hProcess)
        {
            if (System.Environment.Is64BitOperatingSystem)
            {
                if (!ProcessNativeMethods.IsWow64Process(hProcess, out bool wow64))
                {
                    return 32;
                }

                if (wow64)
                {
                    return 32;
                }

                return 64;
            }

            else
            {
                return 32;
            }
        }
    }
}
