﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using ParallelStacks.Runtime;

namespace Microsoft.Diagnostic.Tools.Dump.ExtensionCommands
{
    [Command(Name = "parallelstacks", Help = "Display merged threads stack a la Visual Studio 'Parallel Stacks' panel.")]
    [CommandAlias(Name = "pstacks")]
    public class ParallelStacksCommand : ExtensionCommandBase
    {
        public ClrRuntime Runtime { get; set; }

        [Option(Name = "--allThreads", Help = "Displays all threads per group instead of at most 4 by default.")]
        [OptionAlias(Name = "-a")]
        public bool AllThreads { get; set; }


        public override void Invoke()
        {
            var ps = ParallelStacks.Runtime.ParallelStack.Build(Runtime);
            if (ps == null)
            {
                return;
            }

            int threadIDsCountLimit = AllThreads ? -1 : 4;  // by default, show at most 4 thread ID per group 

            var visitor = new MonoColorConsoleRenderer(Console, limit: threadIDsCountLimit);
            WriteLine("");
            foreach (var stack in ps.Stacks)
            {
                Write("________________________________________________");
                stack.Render(visitor);
                WriteLine("");
                WriteLine("");
                WriteLine("");
            }

            WriteLine($"==> {ps.ThreadIds.Count} threads with {ps.Stacks.Count} roots{Environment.NewLine}");

        }

        protected override string GetDetailedHelp()
        {
            return DetailedHelpText;
        }

        readonly string DetailedHelpText =
    "-------------------------------------------------------------------------------" + Environment.NewLine +
    "ParallelStacks" + Environment.NewLine +
    Environment.NewLine +
    "pstacks groups the callstack of all running threads and shows a merged display a la Visual Studio 'Parallel Stacks' panel" + Environment.NewLine +
    "By default, only 4 threads ID per frame group are listed. Use --allThreads/-a to list all threads ID." + Environment.NewLine +
    Environment.NewLine +
    "> pstacks" + Environment.NewLine +
    "________________________________________________" + Environment.NewLine +
    "~~~~ 8f8c" + Environment.NewLine +
    "    1 (dynamicClass).IL_STUB_PInvoke(IntPtr, Byte*, Int32, Int32 ByRef, IntPtr)" + Environment.NewLine +
    "    ..." + Environment.NewLine +
    "    1 System.Console.ReadLine()" + Environment.NewLine +
    "    1 NetCoreConsoleApp.Program.Main(String[])" + Environment.NewLine +
    Environment.NewLine +
    "________________________________________________" + Environment.NewLine +
    "           ~~~~ 7034" + Environment.NewLine +
    "              1 System.Threading.Monitor.Wait(Object, Int32, Boolean)" + Environment.NewLine +
    "              ..." + Environment.NewLine +
    "              1 System.Threading.Tasks.Task.Wait()" + Environment.NewLine +
    "              1 NetCoreConsoleApp.Program+c.b__1_4(Object)" + Environment.NewLine +
    "           ~~~~ 9c6c,4020" + Environment.NewLine +
    "              2 System.Threading.Monitor.Wait(Object, Int32, Boolean)" + Environment.NewLine +
    "              ..." + Environment.NewLine +
    "                   2 NetCoreConsoleApp.Program+c__DisplayClass1_0.b__7()" + Environment.NewLine +
    "              3 System.Threading.Tasks.Task.InnerInvoke()" + Environment.NewLine +
    "         4 System.Threading.Tasks.Task+c.cctor>b__278_1(Object)" + Environment.NewLine +
    "         ..." + Environment.NewLine +
    "         4 System.Threading.Tasks.Task.ExecuteEntryUnsafe()" + Environment.NewLine +
    "         4 System.Threading.Tasks.Task.ExecuteWorkItem()" + Environment.NewLine +
    "    7 System.Threading.ThreadPoolWorkQueue.Dispatch()" + Environment.NewLine +
    "    7 System.Threading._ThreadPoolWaitCallback.PerformWaitCallback()" + Environment.NewLine +
    Environment.NewLine +
    "==> 8 threads with 2 roots" + Environment.NewLine +
    Environment.NewLine +
    ""
    ;
    }
}
