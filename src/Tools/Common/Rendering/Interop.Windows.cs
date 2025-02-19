// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace System.CommandLine.Rendering
{
    internal static partial class Interop
    {
        public const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

        public const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        public const int STD_OUTPUT_HANDLE = -11;

        public const int STD_INPUT_HANDLE = -10;

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetConsoleMode(IntPtr handle, out uint mode);

        [LibraryImport("kernel32.dll")]
        public static partial uint GetLastError();

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetConsoleMode(IntPtr handle, uint mode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr GetStdHandle(int handle);
    }
}
