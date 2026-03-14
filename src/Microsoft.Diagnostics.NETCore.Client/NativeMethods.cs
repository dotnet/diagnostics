// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        internal static extern bool PeekNamedPipe(
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
            SafePipeHandle hNamedPipe,
            byte[] lpBuffer,
            int bufferSize,
            IntPtr lpBytesRead,
            IntPtr lpTotalBytesAvail,
            IntPtr lpBytesLeftThisMessage
            );
    }
}
