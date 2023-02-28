// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "threadpoolqueue", Aliases = new string[] { "tpq" }, Help = "Displays queued ThreadPool work items.")]
    public class ThreadPoolQueueCommand : ExtensionCommandBase
    {
        public override void ExtensionInvoke()
        {
            var workItems = new Dictionary<string, WorkInfo>();
            int workItemCount = 0;
            var tasks = new Dictionary<string, WorkInfo>();
            int taskCount = 0;

            try
            {
                WriteLine("global work item queue________________________________");
                foreach (var item in Helper.EnumerateGlobalThreadPoolItems())
                {
                    DisplayItem(item, tasks, ref taskCount, workItems, ref workItemCount);
                }
                WriteLine("");

                WriteLine("local per thread work items_____________________________________");
                foreach (var item in Helper.EnumerateLocalThreadPoolItems())
                {
                    DisplayItem(item, tasks, ref taskCount, workItems, ref workItemCount);
                }
                WriteLine("");

                // provide a summary sorted by count
                // tasks first if any
                if (tasks.Values.Count > 0)
                {
                    foreach (var item in tasks.Values.OrderBy(wi => wi.Count))
                    {
                        WriteLine($" {item.Count,4} Task  {item.Name}");
                    }
                    WriteLine(" ----");
                    WriteLine($" {taskCount,4}{Environment.NewLine}");
                }

                // then QueueUserWorkItem next if any
                if (workItems.Values.Count > 0)
                {
                    foreach (var item in workItems.Values.OrderBy(wi => wi.Count))
                    {
                        WriteLine($" {item.Count,4} Work  {item.Name}");
                    }
                    WriteLine(" ----");
                    WriteLine($" {workItemCount,4}{Environment.NewLine}");
                }
            }
            catch (Exception x)
            {
                WriteLine(x.Message);
            }

            WriteLine("");
        }

        private void DisplayItem(ThreadPoolItem item, Dictionary<string, WorkInfo> tasks, ref int taskCount, Dictionary<string, WorkInfo> workItems, ref int workItemCount)
        {
            switch (item.Type)
            {
                case ThreadRoot.Task:
                    WriteLine($"0x{item.Address:X16} Task | {item.MethodName}");
                    UpdateStats(tasks, item.MethodName, ref taskCount);
                    break;

                case ThreadRoot.WorkItem:
                    WriteLine($"0x{item.Address:X16} Work | {item.MethodName}");
                    UpdateStats(workItems, item.MethodName, ref workItemCount);
                    break;

                default:
                    WriteLine($"0x{item.Address:X16} {item.MethodName}");
                    break;
            }
        }

        private static void UpdateStats(Dictionary<string, WorkInfo> stats, string statName, ref int count)
        {
            count++;

            WorkInfo wi;
            if (!stats.ContainsKey(statName))
            {
                wi = new WorkInfo()
                {
                    Name = statName,
                    Count = 0
                };
                stats[statName] = wi;
            }
            else
            {
                wi = stats[statName];
            }

            wi.Count++;
        }

        protected override string GetDetailedHelp()
        {
            return DetailedHelpText;
        }

        private readonly string DetailedHelpText =
    "-------------------------------------------------------------------------------" + Environment.NewLine +
    "ThreadPoolQueue" + Environment.NewLine +
    Environment.NewLine +
    "ThreadPoolQueue lists the enqueued work items in the Clr Thread Pool followed by a summary of the different tasks/work items." + Environment.NewLine +
    "The global queue is first iterated before local per-thread queues." + Environment.NewLine +
    "The name of the method to be called (on which instance if any) is also provided when available." + Environment.NewLine +
    Environment.NewLine +
    "> tpq" + Environment.NewLine +
    Environment.NewLine +
    "global work item queue________________________________" + Environment.NewLine +
    "0x000002AC3C1DDBB0 Work | (ASP.global_asax)System.Web.HttpApplication.ResumeStepsWaitCallback" + Environment.NewLine +
    "                       ..." + Environment.NewLine +
    "0x000002AABEC19148 Task | System.Threading.Tasks.Dataflow.Internal.TargetCore<System.Action>.<ProcessAsyncIfNecessary_Slow>b__3" + Environment.NewLine +
    "" + Environment.NewLine +
    "local per thread work items_____________________________________" + Environment.NewLine +
    "0x000002AE79D80A00 System.Threading.Tasks.ContinuationTaskFromTask" + Environment.NewLine +
    "                       ..." + Environment.NewLine +
    "0x000002AB7CBB84A0 Task | System.Net.Http.HttpClientHandler.StartRequest" + Environment.NewLine +
    "" + Environment.NewLine +
    "   7 Task System.Threading.Tasks.Dataflow.Internal.TargetCore<System.Action>.<ProcessAsyncIfNecessary_Slow>b__3" + Environment.NewLine +
    "                       ..." + Environment.NewLine +
    "  84 Task System.Net.Http.HttpClientHandler.StartRequest" + Environment.NewLine +
    "----" + Environment.NewLine +
    "6039" + Environment.NewLine +
    "" + Environment.NewLine +
    "1810 Work  (ASP.global_asax) System.Web.HttpApplication.ResumeStepsWaitCallback" + Environment.NewLine +
    "----" + Environment.NewLine +
    "1810" + Environment.NewLine +
    ""
    ;

        private class WorkInfo
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }
    }
}