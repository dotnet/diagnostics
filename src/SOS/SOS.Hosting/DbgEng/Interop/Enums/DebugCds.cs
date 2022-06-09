﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CDS : uint
    {
        ALL = 0xffffffff,
        REGISTERS = 1,
        DATA = 2,
        REFRESH = 4 // Inform the GUI clients to refresh debugger windows.
    }
}