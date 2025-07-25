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

        [ServiceImport(Optional = true)]
        public ICrashInfoModuleService CrashInfoFactory { get; set; }

        [Option(Name = "--moduleEnumerationScheme", Aliases = new string[] { "-e" }, Help = "Enables searching modules for the NativeAOT crashinfo data.  Default is None")]
        public ModuleEnumerationScheme ModuleEnumerationScheme { get; set; } = ModuleEnumerationScheme.None;

        public override void Invoke()
        {
            ICrashInfoService crashInfo = CrashInfo ?? CrashInfoFactory.Create(ModuleEnumerationScheme);
            if (crashInfo == null)
            {
                throw new DiagnosticsException("No crash info to display");
            }
            WriteLine($"CrashReason:        {crashInfo.CrashReason}");
            WriteLine($"ThreadId:           {crashInfo.ThreadId:X4}");
            WriteLine($"HResult:            {crashInfo.HResult:X4}");
            WriteLine($"RuntimeType:        {crashInfo.RuntimeType}");
            WriteLine($"RuntimeBaseAddress: {crashInfo.RuntimeBaseAddress:X16}");
            WriteLine($"RuntimeVersion:     {crashInfo.RuntimeVersion}");
            WriteLine($"Message:            {crashInfo.Message}");

            WriteLine();
            WriteLine("** Current Exception **");
            WriteLine();
            IException exception = crashInfo.GetException(0);
            if (exception != null)
            {
                WriteLine("-----------------------------------------------");
                PrintException(exception, string.Empty);
            }

            WriteLine();
            WriteLine($"** Thread {crashInfo.ThreadId} Exception **");
            WriteLine();
            exception = crashInfo.GetThreadException(crashInfo.ThreadId);
            if (exception != null)
            {
                WriteLine("-----------------------------------------------");
                PrintException(exception, string.Empty);
            }

            WriteLine();
            WriteLine("** Nested Exceptions **");
            WriteLine();
            IEnumerable<IException> exceptions = crashInfo.GetNestedExceptions(crashInfo.ThreadId);
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
