// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef _CORDB_DATA_TARGET_
#define _CORDB_DATA_TARGET_

#include <cor.h>
#include <clrdata.h>
#include <cordebug.h>

HRESULT CreateCordbDataTargetFromClrDataTarget(
    ULONG64 moduleBaseAddress,
    ICLRDataTarget* pClrDataTarget,
    ICorDebugDataTarget** ppCorDebugDataTarget);

#endif // _CORDB_DATA_TARGET_
