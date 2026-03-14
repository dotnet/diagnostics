// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("9f50e42c-f136-499e-9a97-73036c94ed2d")]
    public interface IDebugInputCallbacks
    {
        [PreserveSig]
        int StartInput(
            uint BufferSize);

        [PreserveSig]
        int EndInput();
    }
}
