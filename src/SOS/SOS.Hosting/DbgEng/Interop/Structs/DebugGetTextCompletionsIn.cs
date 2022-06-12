// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_GET_TEXT_COMPLETIONS_IN
    {
        public DEBUG_GET_TEXT_COMPLETIONS Flags;
        public uint MatchCountLimit;
        public ulong Reserved0;
        public ulong Reserved1;
        public ulong Reserved2;
    }
}