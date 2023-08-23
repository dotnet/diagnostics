// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal enum ElfSymbolType : byte
    {
        NoType = 0,
        Object = 1,
        Func = 2,
        Section = 3,
        File = 4
    }
}