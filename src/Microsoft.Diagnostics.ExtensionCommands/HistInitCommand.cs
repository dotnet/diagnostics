// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "histinit", Aliases = new[] { "HistInit" }, Help = "Initializes the SOS structures from the stress log saved in the debuggee.")]
    public sealed class HistInitCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public GCHistory History { get; set; }

        public override void Invoke()
        {
            WriteLine("Attempting to read Stress log");

            string failureReason = History.Initialize();
            if (failureReason != null)
            {
                WriteLineError($"{failureReason}");
                WriteLine("FAILURE: Stress log unreadable");
                return;
            }

            using (StringWriter writer = new())
            {
                StressLogFormat.WriteHeader(writer, History);
                Console.Write(writer.ToString());
            }

            WriteLine("SUCCESS: GCHist structures initialized");
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
HistInit

Before running any of the Hist-family commands you need to initialize the SOS
structures from the stress log saved in the debuggee. This is achieved by the
HistInit command, which reads the stress log and prints a summary header.
";
    }
}
