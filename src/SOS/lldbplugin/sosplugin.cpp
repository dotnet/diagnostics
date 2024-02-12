// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "sosplugin.h"

namespace lldb {
    bool PluginInitialize (lldb::SBDebugger debugger);
}

LLDBServices* g_services = nullptr;

bool lldb::PluginInitialize(lldb::SBDebugger debugger)
{
    g_services = new LLDBServices(debugger);
    PluginExtensions::Initialize();
    debugger.GetCommandInterpreter().SetCommandOverrideCallback("quit", PluginExtensions::Uninitialize, nullptr);
    sosCommandInitialize(debugger);
    setsostidCommandInitialize(debugger);
    sethostruntimeCommandInitialize(debugger);
    return true;
}
