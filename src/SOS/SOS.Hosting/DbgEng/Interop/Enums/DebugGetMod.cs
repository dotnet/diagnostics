// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_GETMOD : uint
    {
        DEFAULT = 0,
        NO_LOADED_MODULES = 1,
        NO_UNLOADED_MODULES = 2
    }
}
