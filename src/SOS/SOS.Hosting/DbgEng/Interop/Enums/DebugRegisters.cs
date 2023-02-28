// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_REGISTERS : uint
    {
        DEFAULT = 0,
        INT32 = 1,
        INT64 = 2,
        FLOAT = 4,
        ALL = 7
    }
}
