// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "histstats", Aliases = new[] { "HistStats" }, Help = "Displays stress log stats.")]
    public sealed class HistStatsCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public GCHistory History { get; set; }

        public override void Invoke()
        {
            if (!History.IsInitialized)
            {
                WriteLine("Run !histinit to initialize the GC history from the stress log.");
                return;
            }

            Table output = new(Console, ColumnKind.IntegerWithoutCommas, ColumnKind.IntegerWithoutCommas, ColumnKind.IntegerWithoutCommas);
            output.WriteHeader("GCCount", "Promotes", "Relocs");

            foreach (GCRecord record in History.Records)
            {
                output.WriteRow(record.GCCount, record.Promotes.Count, record.Relocs.Count);
            }

            // Check for duplicate Reloc or Promote messages within one GC.
            bool errorFound = false;
            foreach (GCRecord record in History.Records)
            {
                ulong gcCount = record.GCCount;

                for (int i = 0; i < record.Promotes.Count; i++)
                {
                    for (int j = i + 1; j < record.Promotes.Count; j++)
                    {
                        if (record.Promotes[i].Root == record.Promotes[j].Root)
                        {
                            WriteLine($"Root {record.Promotes[i].Root:x} promoted multiple times in gc {gcCount}");
                            errorFound = true;
                        }
                    }
                }

                for (int i = 0; i < record.Relocs.Count; i++)
                {
                    for (int j = i + 1; j < record.Relocs.Count; j++)
                    {
                        if (record.Relocs[i].Root == record.Relocs[j].Root)
                        {
                            WriteLine($"Root {record.Relocs[i].Root:x} relocated multiple times in gc {gcCount}");
                            errorFound = true;
                        }
                    }
                }
            }

            if (!errorFound)
            {
                WriteLine("No duplicate promote or relocate messages found in the log.");
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
HistStats

Displays per-GC counts of promote and relocate messages reconstructed from the
stress log, then reports any root that was promoted or relocated more than once
within a single GC. Run HistInit first.
";
    }
}
