// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// Native CLR_DEBUG_RESOURCE struct
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ClrDebugResource
    {
        public uint dwVersion;
        public Guid signature;
        public int dwDacTimeStamp;
        public int dwDacSizeOfImage;
        public int dwDbiTimeStamp;
        public int dwDbiSizeOfImage;
    }
}
