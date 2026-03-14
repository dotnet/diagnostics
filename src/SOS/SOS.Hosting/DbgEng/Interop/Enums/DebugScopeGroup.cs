// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_SCOPE_GROUP : uint
    {
        ARGUMENTS = 1,
        LOCALS = 2,
        ALL = 3
    }
}
