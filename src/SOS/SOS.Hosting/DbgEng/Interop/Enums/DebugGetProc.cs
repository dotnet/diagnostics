// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_GET_PROC : uint
    {
        DEFAULT = 0,
        FULL_MATCH = 1,
        ONLY_MATCH = 2,
        SERVICE_NAME = 4
    }
}