// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CONNECT_SESSION : uint
    {
        DEFAULT = 0,
        NO_VERSION = 1,
        NO_ANNOUNCE = 2
    }
}