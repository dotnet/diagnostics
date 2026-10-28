// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef DIAGNOSTICS_PROCESSMODULES_H
#define DIAGNOSTICS_PROCESSMODULES_H

namespace CorUnix
{
    struct ProcessModules
    {
        ProcessModules* _next;
        PVOID _baseAddress;
        PVOID _minimumAddress;
        CHAR _name[0];

        ProcessModules* GetNext() const { return _next; }
        PVOID GetBaseAddress() const { return _baseAddress == 0 ? _minimumAddress : _baseAddress; }
        const CHAR* GetName() const { return _name; }
    };
}

CorUnix::ProcessModules* CreateProcessModules(DWORD processId, LPDWORD count);
void DestroyProcessModules(CorUnix::ProcessModules* modules);

#endif
