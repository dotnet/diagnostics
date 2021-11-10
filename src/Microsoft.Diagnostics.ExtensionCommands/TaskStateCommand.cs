// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using System;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "taskstate", Aliases = new string[] { "tks" }, Help = "Display a Task state in a human readable format.")]
    public class TaskStateCommand : ExtensionCommandBase
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
                if (!TryParseAddress(Address, out var address))
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


        protected override string GetDetailedHelp()
        {
            return DetailedHelpText;
        }

        private readonly string DetailedHelpText =
            "-------------------------------------------------------------------------------" + Environment.NewLine +
            "TaskState [hexa address] [-v <decimal state value>]" + Environment.NewLine +
            Environment.NewLine +
            "TaskState translates a Task m_stateFlags field value into human readable format." + Environment.NewLine +
            "It supports hexadecimal address corresponding to a task instance or -v <decimal state value>." + Environment.NewLine +
            Environment.NewLine +
            "> tks 000001db16cf98f0" + Environment.NewLine +
            "Running" + Environment.NewLine +
            Environment.NewLine +
            "> tks -v 73728" + Environment.NewLine +
            "WaitingToRun"
            ;
    }
}
