// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("67721fe9-56d2-4a44-a325-2b65513ce6eb")]
    public interface IDebugOutputCallbacks2 : IDebugOutputCallbacks
    {
        /* IDebugOutputCallbacks */

        /// <summary>
        /// This method is not used.
        /// </summary>
        [PreserveSig]
        new int Output(
            DEBUG_OUTPUT Mask,
            [In][MarshalAs(UnmanagedType.LPStr)] string Text);

        /* IDebugOutputCallbacks2 */

        [PreserveSig]
        int GetInterestMask(
            out DEBUG_OUTCBI Mask);

        [PreserveSig]
        int Output2(
            DEBUG_OUTCB Which,
            DEBUG_OUTCBF Flags,
            ulong Arg,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Text);
    }
}