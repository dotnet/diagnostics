// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "timerinfo", Aliases = new string[] { "ti" }, Help = "Displays information about running timers.")]
    public class TimersCommand : ExtensionCommandBase
    {
        public override void ExtensionInvoke()
        {
            try
            {
                var stats = new Dictionary<string, TimerStat>(64);
                int totalCount = 0;
                foreach (var timer in Helper.EnumerateTimers().OrderBy(t => t.Period))
                {
                    totalCount++;

                    string line = string.Intern(GetTimerString(timer));
                    string key;
                    if (timer.IsShort.HasValue)
                    {
                        key = string.Intern(string.Format(
                            "({0}) @{1,8} ms every {2,8} ms | {3} ({4}) -> {5}",
                            (timer.IsShort == true) ? "S" : "L",
                            timer.DueTime.ToString(),
                            (timer.Period == 4294967295) ? "  ------" : timer.Period.ToString(),
                            timer.StateAddress.ToString("X16"),
                            timer.StateTypeName,
                            timer.MethodName
                        ));
                    }
                    else
                    {
                        key = string.Intern(string.Format(
                            "@{0,8} ms every {1,8} ms | {2} ({3}) -> {4}",
                            timer.DueTime.ToString(),
                            (timer.Period == 4294967295) ? "  ------" : timer.Period.ToString(),
                            timer.StateAddress.ToString("X16"),
                            timer.StateTypeName,
                            timer.MethodName
                        ));
                    }

                    if (!stats.TryGetValue(key, out var stat))
                    {
                        stat = new TimerStat()
                        {
                            Count = 0,
                            Line = key,
                        };
                        stats[key] = stat;
                    }
                    stat.Count += 1;

                    WriteLine(line);
                }

                // create a summary
                WriteLine($"{Environment.NewLine}   {totalCount.ToString()} timers{Environment.NewLine}-----------------------------------------------");
                foreach (var stat in stats.OrderBy(kvp => kvp.Value.Count))
                {
                    WriteLine($"{stat.Value.Count.ToString(),4} | {stat.Value.Line}");
                }
            }
            catch (Exception x)
            {
                WriteLine(x.Message);
            }
        }

        private static string GetTimerString(TimerInfo timer)
        {
            if (timer.IsShort.HasValue)
            {
                return string.Format(
                    "({0}) 0x{1} @{2,8} ms every {3,8} ms |  {4} ({5}) -> {6}",
                    timer.IsShort.Value ? "S" : "L",
                    timer.TimerQueueTimerAddress.ToString("X16"),
                    timer.DueTime.ToString(),
                    (timer.Period == 4294967295) ? "  ------" : timer.Period.ToString(),
                    timer.StateAddress.ToString("X16"),
                    timer.StateTypeName,
                    timer.MethodName
                );
            }
            else
            {
                return string.Format(
                    "0x{0} @{1,8} ms every {2,8} ms |  {3} ({4}) -> {5}",
                    timer.TimerQueueTimerAddress.ToString("X16"),
                    timer.DueTime.ToString(),
                    (timer.Period == 4294967295) ? "  ------" : timer.Period.ToString(),
                    timer.StateAddress.ToString("X16"),
                    timer.StateTypeName,
                    timer.MethodName
                );
            }

        }

        protected override string GetDetailedHelp()
        {
            return DetailedHelpText;
        }

        private readonly string DetailedHelpText =
            "-------------------------------------------------------------------------------" + Environment.NewLine +
            "TimerInfo" + Environment.NewLine +
            Environment.NewLine +
            "TimerInfo lists all the running timers followed by a summary of the different items." + Environment.NewLine +
            "The name of the method to be called (on which instance if any) is also provided when available." + Environment.NewLine +
            Environment.NewLine +
            "> ti" + Environment.NewLine +
            "0x000001E29BD45848 @     964 ms every     1000 ms |  0x000001E29BD0C828 (Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure.Heartbeat) ->" + Environment.NewLine +
            "0x000001E19BD0F868 @       1 ms every   ------ ms |  0x000001E19BD0F800 (System.Threading.Tasks.Task+DelayPromise) -> System.Threading.Tasks.Task+<>c.<Delay>b__260_1" + Environment.NewLine +
            "0x000001E09BD09B40 @       1 ms every   ------ ms |  0x000001E09BD09AD8 (System.Threading.Tasks.Task+DelayPromise) -> System.Threading.Tasks.Task+<>c.<Delay>b__260_1" + Environment.NewLine +
            "0x000001E29BD58C68 @       1 ms every   ------ ms |  0x000001E29BD58C00 (System.Threading.Tasks.Task+DelayPromise) -> System.Threading.Tasks.Task+<>c.<Delay>b__260_1" + Environment.NewLine +
            "0x000001E29BCB1398 @    5000 ms every   ------ ms |  0x0000000000000000 () -> System.Diagnostics.Tracing.EventPipeController.PollForTracingCommand" + Environment.NewLine +
            Environment.NewLine +
            "   5 timers" + Environment.NewLine +
            "-----------------------------------------------" + Environment.NewLine +
            "   1 | @     964 ms every     1000 ms | (Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure.Heartbeat) ->" + Environment.NewLine +
            "   1 | @    5000 ms every   ------ ms | () -> System.Diagnostics.Tracing.EventPipeController.PollForTracingCommand" + Environment.NewLine +
            "   3 | @       1 ms every   ------ ms | (System.Threading.Tasks.Task+DelayPromise) -> System.Threading.Tasks.Task+<>c.<Delay>b__260_1"
            ;
    }


    internal class TimerStat
    {
        public string Line { get; set; }
        public int Count { get; set; }
    }
}
