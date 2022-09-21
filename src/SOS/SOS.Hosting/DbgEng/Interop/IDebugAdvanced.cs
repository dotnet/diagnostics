// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("f2df5f53-071f-47bd-9de6-5734c3fed689")]
    public interface IDebugAdvanced
    {
        [PreserveSig]
        int GetThreadContext(
            IntPtr Context,
            int ContextSize);

        [PreserveSig]
        int SetThreadContext(
            IntPtr Context,
            int ContextSize);
    }
}