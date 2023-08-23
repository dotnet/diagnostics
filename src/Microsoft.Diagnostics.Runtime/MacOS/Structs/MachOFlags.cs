// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    [Flags]
    internal enum MachOFlags
    {
        NoUndefs = 1,
        IncrLink = 2,
        DylDlink = 4,
        BindatLoad = 8,
        Prebound = 0x10,
        SplitSegs = 0x20,
        LaxyInit = 0x40,
        TwoLevel = 0x80,
        ForceFlag = 0x100,
        NoMultidefs = 0x200,
        NoPrefixPrebinding = 0x400,
        Prebindable = 0x800,
        AllModsBound = 0x1000,
        SubsectionsViaSymbols = 0x2000,
        Canonical = 0x4000,
        WeakDefines = 0x8000,
        BindsToWeak = 0x10000,
        AllowStackExecution = 0x20000,
        RootSafe = 0x40000,
        SetuidSafe = 0x80000,
        NoReExportedDylibs = 0x100000,
        PIE = 0x200000,
        DeadStrippableDylib = 0x400000,
        HasTlvDescriptors = 0x800000,
        NoHeapExecution = 0x1000000,
    }
}