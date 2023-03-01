// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("4c7fd663-c394-4e26-8ef1-34ad5ed3764c")]
    public interface IDebugOutputCallbacksWide
    {
        [PreserveSig]
        int Output(
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Text);
    }
}
