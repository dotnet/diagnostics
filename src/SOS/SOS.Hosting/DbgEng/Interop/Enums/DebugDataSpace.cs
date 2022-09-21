// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_DATA_SPACE : uint
    {
        VIRTUAL = 0,
        PHYSICAL = 1,
        CONTROL = 2,
        IO = 3,
        MSR = 4,
        BUS_DATA = 5,
        DEBUGGER_DATA = 6
    }
}