// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal enum ElfDynamicEntryTag
    {
        Null = 0,
        Hash = 4,
        StrTab = 5,
        SymTab = 6,
        StrSz = 10,
        SymEnt = 11,
        GnuHash = 0x6ffffef5
    }
}