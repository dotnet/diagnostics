// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal enum ElfAuxvType
    {
        Null = 0,           // end of vector
        Base = 7,           // base address of interpreter
    }
}
