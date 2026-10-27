// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __DIAGNOSTICS_BSTR_H__
#define __DIAGNOSTICS_BSTR_H__

#include <pal.h>

STDAPI_(BSTR) SysAllocString(const OLECHAR* value);
STDAPI_(BSTR) SysAllocStringLen(const OLECHAR* value, UINT length);
STDAPI_(BSTR) SysAllocStringByteLen(const char* value, UINT length);
STDAPI_(void) SysFreeString(BSTR value);
STDAPI_(UINT) SysStringLen(BSTR value);
STDAPI_(UINT) SysStringByteLen(BSTR value);

#endif // __DIAGNOSTICS_BSTR_H__
