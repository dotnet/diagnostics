// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SOS.Extensions.NativeAot
{
    internal static class Entrypoints
    {
        private const int E_INVALIDARG = unchecked((int)0x80070057);

        [UnmanagedCallersOnly(EntryPoint = "SOSInitializeExtensions", CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe int Initialize(byte* sosNativePath)
        {
            if (sosNativePath is null)
            {
                return E_INVALIDARG;
            }

            try
            {
                string path = Marshal.PtrToStringAnsi((IntPtr)sosNativePath);
                return path is not null ? HostServices.Initialize(path) : E_INVALIDARG;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return Marshal.GetHRForException(ex);
            }
        }
    }
}
