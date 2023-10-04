// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using ParallelStacks.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "parallelstacks", Aliases = new string[] { "pstacks" }, Help = "Displays the merged threads stack similarly to the Visual Studio 'Parallel Stacks' panel.")]
    public class ParallelStacksCommand : ClrMDHelperCommandBase
    {
        [ServiceImport(Optional = true)]
        public ClrRuntime Runtime { get; set; }

        [Option(Name = "--allthreads", Aliases = new string[] { "-a" }, Help = "Displays all threads per group instead of at most 4 by default.")]
        public bool AllThreads { get; set; }

        public override void Invoke()
        {
            ParallelStack ps = ParallelStack.Build(Runtime);
            if (ps == null)
            {
                return;
            }

            int threadIDsCountLimit = AllThreads ? -1 : 4;  // by default, show at most 4 thread ID per group

            MonoColorConsoleRenderer visitor = new(Console, limit: threadIDsCountLimit);
            WriteLine("");
            foreach (ParallelStack stack in ps.Stacks)
            {
                Write("________________________________________________");
                stack.Render(visitor);
                WriteLine("");
                WriteLine("");
                WriteLine("");
            }

            WriteLine($"==> {ps.ThreadIds.Count} threads with {ps.Stacks.Count} roots{Environment.NewLine}");
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
ParallelStacks

pstacks groups the callstack of all running threads and shows a merged display a la Visual Studio 'Parallel Stacks' panel
By default, only 4 threads ID per frame group are listed. Use --allThreads/-a to list all threads ID.

> pstacks
________________________________________________
~~~~ 8f8c
    1 (dynamicClass).IL_STUB_PInvoke(IntPtr, Byte*, Int32, Int32 ByRef, IntPtr)
    ...
    1 System.Console.ReadLine()
    1 NetCoreConsoleApp.Program.Main(String[])

________________________________________________
           ~~~~ 7034
              1 System.Threading.Monitor.Wait(Object, Int32, Boolean)
              ...
              1 System.Threading.Tasks.Task.Wait()
              1 NetCoreConsoleApp.Program+c.b__1_4(Object)
           ~~~~ 9c6c,4020
              2 System.Threading.Monitor.Wait(Object, Int32, Boolean)
              ...
              2 NetCoreConsoleApp.Program+c__DisplayClass1_0.b__7()
              3 System.Threading.Tasks.Task.InnerInvoke()
         4 System.Threading.Tasks.Task+c.cctor>b__278_1(Object)
         ...
         4 System.Threading.Tasks.Task.ExecuteEntryUnsafe()
         4 System.Threading.Tasks.Task.ExecuteWorkItem()
    7 System.Threading.ThreadPoolWorkQueue.Dispatch()
    7 System.Threading._ThreadPoolWaitCallback.PerformWaitCallback()

==> 8 threads with 2 roots
";
    }
}
