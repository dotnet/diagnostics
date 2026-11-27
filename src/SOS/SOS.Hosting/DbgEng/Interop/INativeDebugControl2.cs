// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SOS.Hosting.DbgEng.Interop
{
    [GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
    [Guid("d4366723-44df-4bed-8c7e-4c05424f4588")]
    public partial interface INativeDebugControl2 : INativeDebugControl
    {
        [PreserveSig] int Reserved95(); // GetCurrentTimeDate
        [PreserveSig] int Reserved96(); // GetCurrentSystemUpTime
        [PreserveSig] int GetDumpFormatFlags(out DEBUG_FORMAT formatFlags);
    }
}
