// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ==++==
// 
 
// 
// ==--==
#ifndef __sildasm_h__
#define __sildasm_h__

#define _BLD_CLR 1
#include "corhlpr.h"
#include "daccess.h"
#include "dacprivate.h"

std::tuple<ULONG, UINT> DecodeILAtPosition(
        IMetaDataImport *pImport, BYTE *buffer, ULONG bufSize,
        ULONG position, UINT indentCount, COR_ILMETHOD_DECODER& header);

#endif // __sildasm_h__
