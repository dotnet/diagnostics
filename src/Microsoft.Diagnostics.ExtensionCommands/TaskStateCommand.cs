// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "taskstate", Aliases = new string[] { "tks" }, Help = "Displays a Task state in a human readable format.")]
    public class TaskStateCommand : ClrMDHelperCommandBase
    {
        [Argument(Help = "The Task instance address.")]
        public string Address { get; set; }

        [Option(Name = "--value", Aliases = new string[] { "-v" }, Help = "<value> is the value of a Task m_stateFlags field.")]
        public ulong? Value { get; set; }

        public override void ExtensionInvoke()
        {
            if (string.IsNullOrEmpty(Address) && !Value.HasValue)
            {
                WriteLine("Missing Task reference address or state value..." + Environment.NewLine);
                return;
            }

            ulong stateFlag = Value.GetValueOrDefault();

            // access the Task state field if the flag is not given as a parameter
            if (!Value.HasValue)
            {
                if (!TryParseAddress(Address, out ulong address))
                {
                    WriteLine("Numeric value expected: either a task address or -v <state value>..." + Environment.NewLine);
                    return;
                }

                // check if it is a task address
                stateFlag = Helper.GetTaskStateFromAddress(address);
                if (stateFlag == 0)
                {
                    WriteLine("Either a valid task address or -v <state value> is expected..." + Environment.NewLine);
                    return;
                }
            }

            string state = ClrMDHelper.GetTaskState(stateFlag);
            if (state != null)
            {
                WriteLine(state);
            }
            else
            {
                WriteLine("Either a task address or a valid state is expected..." + Environment.NewLine);
            }

            WriteLine("");
        }


        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
TaskState [hexa address] [-v <decimal state value>]

TaskState translates a Task m_stateFlags field value into human readable format.
It supports hexadecimal address corresponding to a task instance or -v <decimal state value>.

> tks 000001db16cf98f0
Running

> tks -v 73728
WaitingToRun
";
    }
}
