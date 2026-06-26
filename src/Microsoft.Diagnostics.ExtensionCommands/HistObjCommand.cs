// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "histobj", Aliases = new[] { "HistObj" }, Help = "Displays the chain of GC relocations that may have led to the specified object address.")]
    public sealed class HistObjCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public GCHistory History { get; set; }

        [Argument(Name = "object", Help = "The object address to track backwards through GC relocations.")]
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
                WriteLineError("Usage: histobj <object address>");
                return;
            }

            Table output = new(Console, ColumnKind.IntegerWithoutCommas, ColumnKind.Pointer, ColumnKind.Text);
            output.WriteHeader("GCCount", "Object", "Roots");

            foreach (GCRecord record in History.Records)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (curAddress == 0)
                {
                    break;
                }

                StringBuilder roots = new();
                ulong candidate = curAddress;
                bool firstReloc = true;
                foreach (RelocRecord reloc in record.Relocs)
                {
                    if (reloc.NewValue == curAddress)
                    {
                        roots.Append($"{reloc.Root:x}, ");
                        if (firstReloc)
                        {
                            candidate = reloc.PrevValue;
                            firstReloc = false;
                        }
                        else if (candidate != reloc.PrevValue)
                        {
                            roots.Append("differing reloc values for this object!");
                        }
                    }
                }

                output.WriteRow(record.GCCount, curAddress, roots.ToString());
                curAddress = candidate;
            }
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
HistObj <obj_address>

Examines all stress log relocation records and displays the chain of GC
relocations that may have led to the address passed in as an argument. Each row
shows a GC, the object address at that GC, and the roots that referenced it.
Run HistInit first.
";
    }
}
