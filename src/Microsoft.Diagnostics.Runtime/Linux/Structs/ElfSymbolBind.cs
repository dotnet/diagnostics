// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal enum ElfSymbolBind : byte
    {
        Local = 0,
        Global = 1,
        Weak = 2
    }
}