// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_FILTER_EXEC_OPTION : uint
    {
        BREAK = 0x00000000,
        SECOND_CHANCE_BREAK = 0x00000001,
        OUTPUT = 0x00000002,
        IGNORE = 0x00000003,
        REMOVE = 0x00000004
    }
}
