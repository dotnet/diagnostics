// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.MacOS.Structs
{
    internal enum LoadCommandType
    {
        Segment = 1,
        SymTab,
        SymSeg,
        Thread,
        UnixThread,
        LoadFVMLib,
        IDFVMLib,
        Ident,
        FVMFile,
        PrePage,
        DysymTab,
        LoadDylib,
        IdDylib,
        LoadDylinker,
        IdDylinker,
        PreboundDylib,
        Routines,
        SubFramework,
        SubUmbrella,
        SubClient,
        SubLibrary,
        TwoLevelHints,
        PrebindChksum,
        Segment64 = 0x19,
        Uuid = 0x1b,
    }
}
