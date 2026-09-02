// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <cstdio>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>
#include <lldb/API/LLDB.h>

namespace
{
    void PrintUsage(const char* program)
    {
        std::fprintf(stderr, "Usage: %s [--no-lldbinit] [--batch] [-o command]...\n", program);
    }

    bool ExecuteCommand(lldb::SBCommandInterpreter& interpreter, const std::string& command)
    {
        lldb::SBCommandReturnObject result;
        result.SetImmediateOutputFile(stdout, false);
        result.SetImmediateErrorFile(stderr, false);
        interpreter.HandleCommand(command.c_str(), result, false);
        std::fflush(stdout);
        std::fflush(stderr);

        return result.GetStatus() != lldb::eReturnStatusQuit;
    }
}

int main(int argc, char** argv)
{
    bool batch = false;
    bool sourceInitFiles = true;
    std::vector<std::string> startupCommands;

    for (int index = 1; index < argc; index++)
    {
        if (std::strcmp(argv[index], "--no-lldbinit") == 0)
        {
            sourceInitFiles = false;
        }
        else if (std::strcmp(argv[index], "--batch") == 0)
        {
            batch = true;
        }
        else if (std::strcmp(argv[index], "-o") == 0)
        {
            if (++index == argc)
            {
                std::fprintf(stderr, "Missing command after -o.\n");
                PrintUsage(argv[0]);
                return 2;
            }
            startupCommands.emplace_back(argv[index]);
        }
        else
        {
            std::fprintf(stderr, "Unsupported argument: %s\n", argv[index]);
            PrintUsage(argv[0]);
            return 2;
        }
    }

    lldb::SBDebugger::Initialize();
    lldb::SBDebugger debugger = lldb::SBDebugger::Create(sourceInitFiles);
    if (!debugger.IsValid())
    {
        std::fprintf(stderr, "Failed to initialize LLDB.\n");
        lldb::SBDebugger::Terminate();
        return 1;
    }

    debugger.SetAsync(false);
    debugger.SetInputFileHandle(stdin, false);
    debugger.SetOutputFileHandle(stdout, false);
    debugger.SetErrorFileHandle(stderr, false);

    lldb::SBCommandInterpreter interpreter = debugger.GetCommandInterpreter();
    bool keepRunning = true;
    for (const std::string& command : startupCommands)
    {
        if (!ExecuteCommand(interpreter, command))
        {
            keepRunning = false;
            break;
        }
    }

    if (!batch)
    {
        std::string command;
        while (keepRunning && std::getline(std::cin, command))
        {
            keepRunning = ExecuteCommand(interpreter, command);
        }
    }

    lldb::SBDebugger::Destroy(debugger);
    lldb::SBDebugger::Terminate();
    return 0;
}
