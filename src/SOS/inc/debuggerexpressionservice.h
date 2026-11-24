// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <unknwn.h>

MIDL_INTERFACE("b8e9e3d1-0247-4ea6-8aa1-2dfda28d155a")
IDebuggerExpressionService : public IUnknown
{
public:
    virtual HRESULT STDMETHODCALLTYPE EvaluateExpression(PCSTR expression, PULONG64 value) = 0;
};
