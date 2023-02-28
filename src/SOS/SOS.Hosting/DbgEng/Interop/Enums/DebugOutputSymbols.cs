// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_OUTPUT_SYMBOLS
    {
        DEFAULT = 0,
        NO_NAMES = 1,
        NO_OFFSETS = 2,
        NO_VALUES = 4,
        NO_TYPES = 0x10
    }
}
