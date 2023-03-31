// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_SYMTYPE : uint
    {
        NONE = 0,
        COFF = 1,
        CODEVIEW = 2,
        PDB = 3,
        EXPORT = 4,
        DEFERRED = 5,
        SYM = 6,
        DIA = 7
    }
}
