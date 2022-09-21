// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CSS : uint
    {
        ALL = 0xffffffff,
        LOADS = 1,
        UNLOADS = 2,
        SCOPE = 4,
        PATHS = 8,
        SYMBOL_OPTIONS = 0x10,
        TYPE_OPTIONS = 0x20
    }
}