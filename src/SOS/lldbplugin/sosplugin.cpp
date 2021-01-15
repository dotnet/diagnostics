// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

/// <summary>
/// Internal trace output for extensions library
/// </summary>
void TraceError(PCSTR format, ...)
{
    va_list args;
    va_start(args, format);
    g_services->InternalOutputVaList(DEBUG_OUTPUT_ERROR, format, args);
    va_end(args);
}
