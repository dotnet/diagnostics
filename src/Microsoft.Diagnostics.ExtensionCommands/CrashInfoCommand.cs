// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "crashinfo", Help = "Displays the crash details that created the dump.")]
    public class CrashInfoCommand : CommandBase
    {
        [ServiceImport(Optional = true)]
        public ICrashInfoService CrashInfo { get; set; }

        [ServiceImport]
        public IModuleService ModuleService { get; set; }

        public override void Invoke()
        {
            if (CrashInfo == null)
            {
                throw new DiagnosticsException("No crash info to display");
            }
            WriteLine();

            WriteLine($"CrashReason:        {CrashInfo.CrashReason}");
            WriteLine($"ThreadId:           {CrashInfo.ThreadId:X4}");
            WriteLine($"HResult:            {CrashInfo.HResult:X4}");
            WriteLine($"RuntimeType:        {CrashInfo.RuntimeType}");
            WriteLine($"RuntimeVersion:     {CrashInfo.RuntimeVersion}");
            WriteLine($"Message:            {CrashInfo.Message}");

            if (CrashInfo.Exception != null)
            {
                WriteLine("-----------------------------------------------");
                PrintException(CrashInfo.Exception, string.Empty);
            }
        }

        private void PrintException(IManagedException exception, string indent)
        {
            WriteLine($"{indent}Exception object:   {exception.Address:X16}");
            WriteLine($"{indent}Exception type:     {exception.Type}");
            WriteLine($"{indent}HResult:            {exception.HResult:X8}");
            WriteLine($"{indent}Message:            {exception.Message}");

            if (exception.Stack != null && exception.Stack.Any())
            {
                WriteLine($"{indent}StackTrace:");
                WriteLine($"{indent}    IP               Function");
                foreach (IStackFrame frame in exception.Stack)
                {
                    string moduleName = "<unknown_module>";
                    if (frame.ModuleBase != 0)
                    {
                        IModule module = ModuleService.GetModuleFromBaseAddress(frame.ModuleBase);
                        if (module != null)
                        {
                            moduleName = Path.GetFileName(module.FileName);
                        }
                    }
                    string methodName = frame.MethodName ?? "<unknown>";
                    WriteLine($"{indent}    {frame.InstructionPointer:X16} {moduleName}!{methodName} + 0x{frame.Offset:X}");
                }
            }

            if (exception.InnerExceptions != null)
            {
                WriteLine("InnerExceptions:");
                foreach (IManagedException inner in exception.InnerExceptions)
                {
                    WriteLine("-----------------------------------------------");
                    PrintException(inner, "    ");
                }
            }
        }
    }
}
