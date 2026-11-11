// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "histroot", Aliases = new[] { "HistRoot" }, Help = "Displays information related to both promotions and relocations of the specified root.")]
    public sealed class HistRootCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public GCHistory History { get; set; }

        [Argument(Name = "root", Help = "The root address to track (as reported by HistObjFind).")]
        public string RootAddress { get; set; }

        public override void Invoke()
        {
            if (!History.IsInitialized)
            {
                WriteLine("Run !histinit to initialize the GC history from the stress log.");
                return;
            }

            if (string.IsNullOrWhiteSpace(RootAddress) || !TryParseAddress(RootAddress, out ulong root))
            {
                WriteLineError("Usage: histroot <root address>");
                return;
            }

            Table output = new(Console, ColumnKind.IntegerWithoutCommas, ColumnKind.Pointer, ColumnKind.Pointer, ColumnKind.Text, ColumnKind.Text);
            output.WriteHeader("GCCount", "Value", "MT", "Promoted?", "Notes");

            bool boring = false;
            foreach (GCRecord record in History.Records)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                PromoteRecord? promote = null;
                bool promotedMoreThanOnce = false;
                foreach (PromoteRecord p in record.Promotes)
                {
                    if (p.Root == root)
                    {
                        if (promote.HasValue)
                        {
                            promotedMoreThanOnce = true;
                        }
                        else
                        {
                            promote = p;
                        }
                    }
                }

                RelocRecord? reloc = null;
                bool relocatedMoreThanOnce = false;
                foreach (RelocRecord r in record.Relocs)
                {
                    if (r.Root == root)
                    {
                        if (reloc.HasValue)
                        {
                            relocatedMoreThanOnce = true;
                        }
                        else
                        {
                            reloc = r;
                        }
                    }
                }

                if (reloc.HasValue)
                {
                    boring = false;

                    string notes = "";
                    if (promote.HasValue)
                    {
                        if (promote.Value.Value != reloc.Value.PrevValue || promote.Value.MethodTable != reloc.Value.MethodTable)
                        {
                            notes += "promote/reloc records in error ";
                        }

                        if (promotedMoreThanOnce || relocatedMoreThanOnce)
                        {
                            notes += "Duplicate promote/relocs";
                        }
                    }

                    output.WriteRow(record.GCCount, reloc.Value.NewValue, reloc.Value.MethodTable, promote.HasValue ? "yes" : "no", notes);
                }
                else if (promote.HasValue)
                {
                    WriteLine($"Error: There is a promote record for root {promote.Value.Root:x}, but no relocation record");
                }
                else if (!boring)
                {
                    WriteLine("...");
                    boring = true;
                }
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
HistRoot <root>

The root value obtained from HistObjFind can be used to track the movement of an
object through the GCs. HistRoot reports both the promotions and relocations of
the specified root across all GCs in the stress log. Run HistInit first.
";
    }
}
