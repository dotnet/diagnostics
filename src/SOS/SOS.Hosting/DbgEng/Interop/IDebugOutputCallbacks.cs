// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("4bf58045-d654-4c40-b0af-683090f356dc")]
    public interface IDebugOutputCallbacks
    {
        [PreserveSig]
        int Output(
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Text);
    }
}
