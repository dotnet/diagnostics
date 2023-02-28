// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_ASMOPT : uint
    {
        DEFAULT = 0x00000000,
        VERBOSE = 0x00000001,
        NO_CODE_BYTES = 0x00000002,
        IGNORE_OUTPUT_WIDTH = 0x00000004,
        SOURCE_LINE_NUMBER = 0x00000008
    }
}
