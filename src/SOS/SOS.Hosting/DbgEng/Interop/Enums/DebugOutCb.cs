// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_OUTCB : uint
    {
        TEXT = 0,
        DML = 1,
        EXPLICIT_FLUSH = 2
    }
}
