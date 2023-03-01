// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Hosting.DbgEng
{
    internal unsafe class DebugAdvanced
    {
        internal DebugAdvanced(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugAdvanced).GUID, validate: true);
            builder.AddMethod(new GetThreadContextDelegate(soshost.GetThreadContext));
            builder.AddMethod(new SetThreadContextDelegate(SOSHost.SetThreadContext));
            builder.Complete();
        }

        #region IDebugAdvanced Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadContextDelegate(
            [In] IntPtr self,
            [In] IntPtr context,
            [In] uint contextSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetThreadContextDelegate(
            [In] IntPtr self,
            [In] IntPtr context,
            [In] uint contextSize);

        #endregion
    }
}
