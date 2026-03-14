// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// Util_NoDependencies.cpp
//

//
// This contains a bunch of C++ utility classes needed also for UtilCode without dependencies
// (standalone version without CLR/clr.dll/mscoree.dll dependencies).
//
//*****************************************************************************

#include "stdafx.h"
#include "utilcode.h"
#include "ex.h"

void OutputDebugStringUtf8(LPCUTF8 utf8DebugMsg)
{
#ifdef TARGET_UNIX
    OutputDebugStringA(utf8DebugMsg);
#else
    if (utf8DebugMsg == NULL)
        utf8DebugMsg = "";

    MAKE_WIDEPTR_FROMUTF8_NOTHROW(wideDebugMsg, utf8DebugMsg);
    OutputDebugStringW(wideDebugMsg);
#endif // !TARGET_UNIX
}
