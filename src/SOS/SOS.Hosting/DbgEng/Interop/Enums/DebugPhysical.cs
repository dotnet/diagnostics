// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_PHYSICAL : uint
    {
        DEFAULT = 0,
        CACHED = 1,
        UNCACHED = 2,
        WRITE_COMBINED = 3
    }
}
