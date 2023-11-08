// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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

        public override void Invoke()
        {
            if (CrashInfo == null)
            {
                throw new DiagnosticsException("No crash info to display");
            }
            WriteLine($"CrashReason:        {CrashInfo.CrashReason}");
            WriteLine($"ThreadId:           {CrashInfo.ThreadId:X4}");
            WriteLine($"HResult:            {CrashInfo.HResult:X4}");
            WriteLine($"RuntimeType:        {CrashInfo.RuntimeType}");
            WriteLine($"RuntimeBaseAddress: {CrashInfo.RuntimeBaseAddress:X16}");
            WriteLine($"RuntimeVersion:     {CrashInfo.RuntimeVersion}");
            WriteLine($"Message:            {CrashInfo.Message}");

            WriteLine();
            WriteLine("** Current Exception **");
            WriteLine();
            IException exception = CrashInfo.GetException(0);
            if (exception != null)
            {
                WriteLine("-----------------------------------------------");
                PrintException(exception, string.Empty);
            }

            WriteLine();
            WriteLine($"** Thread {CrashInfo.ThreadId} Exception **");
            WriteLine();
            exception = CrashInfo.GetThreadException(CrashInfo.ThreadId);
            if (exception != null)
            {
                WriteLine("-----------------------------------------------");
                PrintException(exception, string.Empty);
            }

            WriteLine();
            WriteLine("** Nested Exceptions **");
            WriteLine();
            IEnumerable<IException> exceptions = CrashInfo.GetNestedExceptions(CrashInfo.ThreadId);
            foreach (IException ex in exceptions)
            {
                WriteLine("-----------------------------------------------");
                PrintException(ex, string.Empty);
            }
        }

        private void PrintException(IException exception, string indent)
        {
            WriteLine($"{indent}Exception object:   {exception.Address:X16}");
            WriteLine($"{indent}Exception type:     {exception.Type}");
            WriteLine($"{indent}HResult:            {exception.HResult:X8}");
            WriteLine($"{indent}Message:            {exception.Message}");

            IStack stack = exception.Stack;
            if (stack.FrameCount > 0)
            {
                WriteLine($"{indent}StackTrace:");
                WriteLine($"{indent}    IP               Function");
                for (int index = 0; index < stack.FrameCount; index++)
                {
                    IStackFrame frame = stack.GetStackFrame(index);
                    frame.GetMethodName(out string moduleName, out string methodName, out ulong displacement);
                    WriteLine($"{indent}    {frame.InstructionPointer:X16} {moduleName ?? "<unknown_module>"}!{methodName ?? "<unknown>"} + 0x{displacement:X}");
                }
            }

            if (exception.InnerExceptions.Any())
            {
                WriteLine($"{indent}InnerExceptions:");
                foreach (IException inner in exception.InnerExceptions)
                {
                    WriteLine($"{indent}-----------------------------------------------");
                    PrintException(inner, $"{indent}    ");
                }
            }
        }
    }
}
