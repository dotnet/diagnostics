// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "histclear", Aliases = new[] { "HistClear" }, Help = "Releases any resources used by the Hist-family of commands.")]
    public sealed class HistClearCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public GCHistory History { get; set; }

        public override void Invoke()
        {
            History.Clear();
            WriteLine("Completed successfully.");
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
HistClear

Releases any resources used by the Hist-family of commands. Generally there is
no need to call this explicitly, as each HistInit first cleans up the previous
resources.
";
    }
}
