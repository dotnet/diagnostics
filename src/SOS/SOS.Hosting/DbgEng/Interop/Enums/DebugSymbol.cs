// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_SYMBOL : uint
    {
        EXPANSION_LEVEL_MASK = 0xf,
        EXPANDED = 0x10,
        READ_ONLY = 0x20,
        IS_ARRAY = 0x40,
        IS_FLOAT = 0x80,
        IS_ARGUMENT = 0x100,
        IS_LOCAL = 0x200
    }
}