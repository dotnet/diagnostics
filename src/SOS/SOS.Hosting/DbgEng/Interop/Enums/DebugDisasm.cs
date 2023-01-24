// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_DISASM : uint
    {
        EFFECTIVE_ADDRESS = 1,
        MATCHING_SYMBOLS = 2,
        SOURCE_LINE_NUMBER = 4,
        SOURCE_FILE_NAME = 8
    }
}