// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// The kind of ELF note.
    /// </summary>
    internal enum ElfNoteType : uint
    {
        PrpsStatus = 1,
        PrpsFpreg = 2,
        PrpsInfo = 3,
        TASKSTRUCT = 4,
        Aux = 6,
        File = 0x46494c45 // "FILE" in ascii
    }
}
