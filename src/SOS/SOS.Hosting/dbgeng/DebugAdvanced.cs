﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Runtime.InteropServices;

namespace SOS
{
    internal unsafe class DebugAdvanced
    {
        internal DebugAdvanced(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugAdvanced).GUID, validate: true);
            builder.AddMethod(new GetThreadContextDelegate(soshost.GetThreadContext));
            builder.AddMethod(new SetThreadContextDelegate(soshost.SetThreadContext));
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