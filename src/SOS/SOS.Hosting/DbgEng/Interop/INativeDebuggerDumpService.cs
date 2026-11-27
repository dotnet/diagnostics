// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SOS.Hosting.DbgEng.Interop
{
    [GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
    [Guid("7b52c66b-b09d-45d0-b871-9c113db3c088")]
    public partial interface INativeDebuggerDumpService
    {
        [PreserveSig]
        int IsMinidumpSafeModeEnabled();
    }
}
