// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WDBGEXTS_CLR_DATA_INTERFACE
    {
        public Guid* Iid;
        private readonly void* _iface;

        public WDBGEXTS_CLR_DATA_INTERFACE(Guid* iid)
        {
            Iid = iid;
            _iface = null;
        }

        public object Interface => _iface != null ? Marshal.GetObjectForIUnknown((IntPtr)_iface) : null;
    }
}