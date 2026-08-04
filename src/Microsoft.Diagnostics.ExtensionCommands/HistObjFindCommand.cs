// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "histobjfind", Aliases = new[] { "HistObjFind", "hof" }, Help = "Displays all the log entries that reference an object at the specified address.")]
    public sealed class HistObjFindCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public GCHistory History { get; set; }

        [Argument(Name = "object", Help = "The object address to search for in the stress log.")]
        public string ObjectAddress { get; set; }

        public override void Invoke()
        {
            if (!History.IsInitialized)
            {
                WriteLine("Run !histinit to initialize the GC history from the stress log.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ObjectAddress) || !TryParseAddress(ObjectAddress, out ulong curAddress))
            {
                WriteLineError("Usage: histobjfind <object address>");
                return;
            }

            Table output = new(Console, ColumnKind.IntegerWithoutCommas, ColumnKind.Pointer, ColumnKind.Text);
            output.WriteHeader("GCCount", "Object", "Message");

            bool boring = false;
            foreach (GCRecord record in History.Records)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (curAddress == 0)
                {
                    break;
                }

                foreach (PromoteRecord promote in record.Promotes)
                {
                    if (promote.Value == curAddress)
                    {
                        boring = false;
                        output.WriteRow(record.GCCount, curAddress, $"Promotion for root {promote.Root:x} (MT = {promote.MethodTable:x})");
                    }
                }

                foreach (RelocRecord reloc in record.Relocs)
                {
                    if (reloc.NewValue == curAddress || reloc.PrevValue == curAddress)
                    {
                        boring = false;
                        string which = reloc.NewValue == curAddress ? "NEWVALUE" : "PREVVALUE";
                        output.WriteRow(record.GCCount, curAddress, $"Relocation {which} for root {reloc.Root:x}");
                    }
                }

                if (!boring)
                {
                    WriteLine("...");
                    boring = true;
                }
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
HistObjFind <obj_address>

Examines log entries related to an object whose present address is known. The
output contains all stress log entries (promotions and relocations) that
reference the object. Run HistInit first.
";
    }
}
