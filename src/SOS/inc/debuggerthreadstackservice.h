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
    ULONG64 StackPointer;
    BOOL StackPointerValid;
} DEBUGGER_STACK_FRAME, *PDEBUGGER_STACK_FRAME;

MIDL_INTERFACE("AB73D0E6-A5E0-4B5C-B9C1-B312C73C39EE")
IDebuggerThreadStackService : public IUnknown
{
public:
    virtual HRESULT STDMETHODCALLTYPE GetThreadStackTrace(
        ULONG32 sysId,
        PDEBUGGER_STACK_FRAME frames,
        ULONG framesSize,
        PULONG framesFilled) = 0;
};

#ifdef __cplusplus
};
#endif
