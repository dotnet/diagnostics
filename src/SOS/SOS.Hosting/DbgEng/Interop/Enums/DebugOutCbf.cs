// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_OUTCBF : uint
    {
        EXPLICIT_FLUSH = 1,
        DML_HAS_TAGS = 2,
        DML_HAS_SPECIAL_CHARACTERS = 4
    }
}