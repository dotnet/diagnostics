﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.FileFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats.ELF
{
    public enum ELFNoteType
    {
        PrpsInfo = 3, // NT_PRPSINFO
        GnuBuildId = 3, // NT_GNU_BUILD_ID
        File = 0x46494c45 // "FILE" in ascii
    }

    public class ELFNoteHeader : TStruct
    {
        public uint NameSize;
        public uint ContentSize;
        public ELFNoteType Type;
    }
}
