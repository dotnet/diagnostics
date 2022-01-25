// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Process = System.Diagnostics.Process;
using System.IO;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.CommandLine.Binding;

namespace Microsoft.Internal.Common.Commands
{
    public class ProcessStatusCommandHandler
    {
        delegate Task<int> ProcessStatusDelegate(IConsole console, bool verbose);
        public static Command ProcessStatusCommand() =>
            new Command(
                name: "ps", 
                description: "Lists the dotnet processes that traces can be collected")
            {
                HandlerDescriptor.FromDelegate((ProcessStatusDelegate)ProcessStatus).GetCommandHandler(),
                VerboseOption()
            };

        public static Option VerboseOption() =>
            new Option(
                aliases: new[] {"-v", "--verbose"},
                description: "Output includes commandline arguments.")
                {
                    Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
                };

        /// <summary>
        /// Print the current list of available .NET core processes for diagnosis, their statuses and the command line arguments that are passed to them.
        /// </summary>
        public static Task<int> ProcessStatus(IConsole console, bool verbose)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                var processes = DiagnosticsClient.GetPublishedProcesses()
                    .Select(GetProcessById)
                    .Where(process => process != null)
                    .OrderBy(process => process.ProcessName)
                    .ThenBy(process => process.Id);

                var currentPid = Process.GetCurrentProcess().Id;

                foreach (var process in processes)
                {
                    if (process.Id == currentPid)
                    {
                        continue;
                    }
                    if(verbose)
                    {
                        try
                        {
                            String cmdLineArgs = GetArgs(process);
                            cmdLineArgs = cmdLineArgs == process.MainModule?.FileName ? "" : cmdLineArgs;
                            string fileName = process.MainModule?.FileName ?? "";
                            string[] cmdList = cmdLineArgs.Split(" ");
                            foreach(string str in cmdList)
                            {
                                string splitChar = "";
                                if (str.Contains("/"))
                                {
                                    splitChar = "/";
                                    
                                }
                                else if(str.Contains("\\"))
                                {
                                    splitChar = "\\";
                                }
                                else
                                {
                                    Console.WriteLine($"{str}");
                                    continue;
                                }
                                string[] filePath = str.Split(splitChar);
                                fileName = filePath.LastOrDefault();
                                break;
                            }
                            string toAppend = $"{process.Id, 10} {process.ProcessName, -10} {fileName, -10} {cmdLineArgs, -10}\n";
                            if (toAppend.Length > 140)
                            {
                                toAppend = $"{process.Id, 10} {fileName, -10} {cmdLineArgs, -10}\n";
                            }
                            if (toAppend.Length > 120)
                            {
                                toAppend = $"{process.Id, 10} {cmdLineArgs, -10}\n";
                            }
                            sb.Append(toAppend);

                        }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                        {
                            sb.Append($"{process.Id, 10} {process.ProcessName, -10} [Elevated process - cannot determine path] [Elevated process - cannot determine commandline arguments]\n");
                        }
                    }
                }
                console.Out.WriteLine(sb.ToString());
            }
            catch (InvalidOperationException ex)
            {
                console.Out.WriteLine(ex.ToString());
            }
            return Task.FromResult(0);
        }

        private static Process GetProcessById(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string GetArgs(Process process)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    string commandLine = WindowsProcessExtension.GetCommandLine(process);
                    if (!String.IsNullOrWhiteSpace(commandLine))
                    {
                        string[] commandLineSplit = commandLine.Split(' ');
                        if (commandLineSplit.FirstOrDefault() == process.ProcessName)
                        {
                            return String.Join(" ", commandLineSplit.Skip(1));
                        }
                        return commandLine;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                {
                    return "[Elevated process - cannot determine command line arguments]";
                }

            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    string commandLine = File.ReadAllText($"/proc/{process.Id}/cmdline");
                    if(!String.IsNullOrWhiteSpace(commandLine))
                    {
                        //The command line may be modified and the first part of the command line may not be /path/to/exe. If that is the case, return the command line as is.Else remove the path to module as we are already displaying that.
                        string[] commandLineSplit = commandLine.Split('\0');
                        if (commandLineSplit.FirstOrDefault() == process.MainModule?.FileName)
                        {
                            return String.Join(" ", commandLineSplit.Skip(1));
                        }
                        return commandLine.Replace("\0", " ");
                    }
                    return "";
                }
                catch (IOException)
                {
                    return "[cannot determine command line arguments]";
                }
            }
            return "";
        }
    }

    internal static class WindowsProcessExtension
    {

        //https://github.com/projectkudu/kudu/blob/787c893a9336beb498252bb2f90a06a95763f9e9/Kudu.Core/Infrastructure/ProcessExtensions.cs#L562-L616

        static public string GetCommandLine(Process process)
        {
            IntPtr processHandle;
            try 
            {
                processHandle = process.Handle;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException)
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

                IntPtr ptr;

                if (!ReadIntPtr(processHandle, pPeb + offset, out ptr))
                {
                    return "[cannot determine command line arguments]";
                }

                if ((processBitness == 64 && System.Environment.Is64BitProcess)||
                    (processBitness == 32 && !System.Environment.Is64BitProcess))
                {
                    //System and Process are both the same bitness.

                    ProcessNativeMethods.UNICODE_STRING unicodeString = new ProcessNativeMethods.UNICODE_STRING();
                    if(!ProcessNativeMethods.ReadProcessMemory(processHandle, ptr+unicodeStringOffset, ref unicodeString, new IntPtr(Marshal.SizeOf(unicodeString)), IntPtr.Zero))
                    {
                        return "[cannot determine command line arguments]";
                    }

                    commandLineLength = unicodeString.Length;

                    commandLineBuffer = unicodeString.Buffer;
                }

                else 
                {
                    //System is 64 bit and the process is 32 bit

                    ProcessNativeMethods.UNICODE_STRING_32 unicodeString32 = new ProcessNativeMethods.UNICODE_STRING_32();

                    if(!ProcessNativeMethods.ReadProcessMemory(processHandle, ptr+unicodeStringOffset, ref unicodeString32, new IntPtr(Marshal.SizeOf(unicodeString32)), IntPtr.Zero))
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

            catch(Win32Exception)
            {
                return "[cannot determine command line arguments]";
            }

        }

        private static bool ReadIntPtr(IntPtr hProcess, IntPtr ptr, out IntPtr readPtr)
        {
            var dataSize = new IntPtr(IntPtr.Size);
            var res_len = IntPtr.Zero;
            if (!ProcessNativeMethods.ReadProcessMemory(
                hProcess,
                ptr,
                out readPtr,
                dataSize,
                ref res_len))
            {
                throw new Win32Exception("Reading of the pointer failed. Error: "+Marshal.GetLastWin32Error());
            }

            // This is more like an assert
            return res_len == dataSize;
        }


        private static IntPtr GetPebNative(IntPtr hProcess)
        {
            var pbi = new ProcessNativeMethods.ProcessInformation();
            int res_len = 0;
            int pbiSize = Marshal.SizeOf(pbi);
            ProcessNativeMethods.NtQueryInformationProcess(
                hProcess,
                ProcessNativeMethods.ProcessBasicInformation,
                ref pbi,
                pbiSize,
                out res_len);

            if (res_len != pbiSize)
            {
                throw new Win32Exception("Query Information Process failed. Error: "+ Marshal.GetLastWin32Error());
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
                var ptr = IntPtr.Zero;
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

        static private int GetProcessBitness(IntPtr hProcess)
        {
            if (System.Environment.Is64BitOperatingSystem)
            {
                bool wow64;
                if (!ProcessNativeMethods.IsWow64Process(hProcess, out wow64))
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

    internal static class ProcessNativeMethods
    {
        public const int ProcessBasicInformation = 0;
        public const int ProcessWow64Information = 26;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            IntPtr dwSize,
            ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            out IntPtr lpPtr,
            IntPtr dwSize,
            ref IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            ref UNICODE_STRING lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            ref UNICODE_STRING_32 lpBuffer,
            IntPtr dwSize,
            IntPtr lpNumberOfBytesRead);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(
            IntPtr hProcess,
            UInt32 dwDesiredAccess,
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

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)]out bool wow64Process);

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

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
                IntPtr processHandle,
                int processInformationClass,
                ref ProcessInformation processInformation,
                int processInformationLength,
                out int returnLength);

        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref IntPtr processInformation,
            int processInformationLength,
            ref int returnLength);
    }
}
