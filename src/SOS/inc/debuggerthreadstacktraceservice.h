// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct _DEBUGGER_STACK_FRAME
{
    ULONG64 InstructionPointer;
    ULONG64 StackPointer; // Zero when unavailable.
} DEBUGGER_STACK_FRAME, *PDEBUGGER_STACK_FRAME;

MIDL_INTERFACE("3F0DEFDA-A8A3-43B2-9209-935147C89B58")
IDebuggerThreadStackTraceService : public IUnknown
{
public:
    virtual HRESULT STDMETHODCALLTYPE GetDebuggerStackTrace(
        ULONG32 sysId,
        PDEBUGGER_STACK_FRAME frames,
        ULONG framesSize,
        PULONG framesFilled) = 0;
};

#ifdef __cplusplus
};
#endif
