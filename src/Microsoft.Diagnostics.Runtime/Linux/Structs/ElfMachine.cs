// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// The ELF machine type
    /// </summary>
    internal enum ElfMachine : ushort
    {
        EM_NONE = 0,            /* No machine */
        EM_386 = 3,             /* Intel 80386 */
        EM_PARISC = 15,         /* HPPA */
        EM_SPARC32PLUS = 18,    /* Sun's "v8plus" */
        EM_PPC = 20,            /* PowerPC */
        EM_PPC64 = 21,          /* PowerPC64 */
        EM_SPU = 23,            /* Cell BE SPU */
        EM_ARM = 40,            /* ARM */
        EM_SH = 42,             /* SuperH */
        EM_SPARCV9 = 43,        /* SPARC v9 64-bit */
        EM_IA_64 = 50,          /* HP/Intel IA-64 */
        EM_X86_64 = 62,         /* AMD x86-64 */
        EM_S390 = 22,           /* IBM S/390 */
        EM_CRIS = 76,           /* Axis Communications 32-bit embedded processor */
        EM_V850 = 87,           /* NEC v850 */
        EM_M32R = 88,           /* Renesas M32R */
        EM_H8_300 = 46,         /* Renesas H8/300,300H,H8S */
        EM_MN10300 = 89,        /* Panasonic/MEI MN10300, AM33 */
        EM_BLACKFIN = 106,      /* ADI Blackfin Processor */
        EM_AARCH64 = 183,       /* ARM AARCH64 */
        EM_FRV = 0x5441,        /* Fujitsu FR-V */
        EM_AVR32 = 0x18ad       /* Atmel AVR32 */
    }
}
