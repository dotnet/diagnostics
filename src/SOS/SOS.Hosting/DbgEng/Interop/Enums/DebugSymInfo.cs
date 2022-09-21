// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_SYMINFO : uint
    {
        BREAKPOINT_SOURCE_LINE = 0,
        IMAGEHLP_MODULEW64 = 1,
        GET_SYMBOL_NAME_BY_OFFSET_AND_TAG_WIDE = 2,
        GET_MODULE_SYMBOL_NAMES_AND_OFFSETS = 3
    }
}