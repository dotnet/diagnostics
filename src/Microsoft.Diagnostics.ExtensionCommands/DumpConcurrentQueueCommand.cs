// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpconcurrentqueue", Aliases = new string[] { "dcq" }, Help = "Displays concurrent queue content.")]
    public class DumpConcurrentQueueCommand : ClrMDHelperCommandBase
    {
        [Argument(Help = "The address of a ConcurrentQueue object.")]
        public string Address { get; set; }

        [ServiceImport(Optional = true)]
        public ClrRuntime Runtime { get; set; }

        public override void ExtensionInvoke()
        {
            if (string.IsNullOrEmpty(Address))
            {
                throw new DiagnosticsException("Missing ConcurrentQueue address...");
            }

            if (!TryParseAddress(Address, out ulong address))
            {
                throw new DiagnosticsException("Hexadecimal address expected...");
            }

            ClrHeap heap = Runtime.Heap;
            ClrType type = heap.GetObjectType(address);
            if (type == null)
            {
                throw new DiagnosticsException($"{Address:x16} is not referencing an object...");
            }

            if (!type.Name.StartsWith("System.Collections.Concurrent.ConcurrentQueue<"))
            {
                throw new DiagnosticsException($"{Address:x16} is not a ConcurrentQueue but an instance of {type.Name}...");
            }

            WriteLine($"{type.Name}");
            try
            {
                int count = 0;
                foreach (string item in Helper.EnumerateConcurrentQueue(address))
                {
                    count++;
                    WriteLine($"{count,4} - {item}");
                }
                WriteLine("---------------------------------------------" + Environment.NewLine + $"{count} items");
            }
            catch (Exception x)
            {
                WriteLine(x.Message);
            }

            WriteLine("");
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"-------------------------------------------------------------------------------
DumpConcurrentQueue

Lists all items in the given concurrent queue.

For simple types such as numbers, boolean and string, values are shown.
> dcq 00000202a79320e8
System.Collections.Concurrent.ConcurrentQueue<System.Int32>
   1 - 0
   2 - 1
   3 - 2

In case of reference types, the command to dump each object is shown.
> dcq 00000202a79337f8
System.Collections.Concurrent.ConcurrentQueue<ForDump.ReferenceType>
   1 - dumpobj 0x202a7934e38
   2 - dumpobj 0x202a7934fd0
   3 - dumpobj 0x202a7935078

For value types, the command to dump each array segment is shown.
The next step is to manually dump each element with dumpvc <the Element Methodtable> <[item] address>.
> dcq 00000202a7933370
System.Collections.Concurrent.ConcurrentQueue<ForDump.ValueType>
   1 - dumparray 202a79334e0
   2 - dumparray 202a7938a88
";
    }
}
