// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpconcurrentqueue", Aliases = new string[] { "dcq" }, Help = "Displays concurrent queue content.")]
    public class DumpConcurrentQueueCommand : ExtensionCommandBase
    {
        [Argument(Help = "The address of a ConcurrentQueue object.")]
        public string Address { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public override void ExtensionInvoke()
        {
            if (string.IsNullOrEmpty(Address))
            {
                WriteLine("Missing ConcurrentQueue address...");
                return;
            }

            if (!TryParseAddress(Address, out var address))
            {
                WriteLine("Hexadecimal address expected...");
                return;
            }

            var heap = Runtime.Heap;
            var type = heap.GetObjectType(address);
            if (type == null)
            {
                WriteLine($"{Address:x16} is not referencing an object...");
                return;
            }


            if (!type.Name.StartsWith("System.Collections.Concurrent.ConcurrentQueue<"))
            {
                WriteLine($"{Address:x16} is not a ConcurrentQueue but an instance of {type.Name}...");
                return;
            }

            WriteLine($"{type.Name}");
            try
            {
                int count = 0;
                foreach (var item in Helper.EnumerateConcurrentQueue(address))
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

        protected override string GetDetailedHelp()
        {
            return DetailedHelpText;
        }

        readonly string DetailedHelpText =
    "-------------------------------------------------------------------------------" + Environment.NewLine +
    "DumpConcurrentQueue" + Environment.NewLine +
    Environment.NewLine +
    "Lists all items in the given concurrent queue." + Environment.NewLine +
    Environment.NewLine +
    "For simple types such as numbers, boolean and string, values are shown." + Environment.NewLine +
    "> dcq 00000202a79320e8" + Environment.NewLine +
    "System.Collections.Concurrent.ConcurrentQueue<System.Int32>" + Environment.NewLine +
    "   1 - 0" + Environment.NewLine +
    "   2 - 1" + Environment.NewLine +
    "   3 - 2" + Environment.NewLine +
    Environment.NewLine +
    "In case of reference types, the command to dump each object is shown." + Environment.NewLine +
    "> dcq 00000202a79337f8" + Environment.NewLine +
    "System.Collections.Concurrent.ConcurrentQueue<ForDump.ReferenceType>" + Environment.NewLine +
    "   1 - dumpobj 0x202a7934e38" + Environment.NewLine +
    "   2 - dumpobj 0x202a7934fd0" + Environment.NewLine +
    "   3 - dumpobj 0x202a7935078" + Environment.NewLine +
    Environment.NewLine +
    "For value types, the command to dump each array segment is shown." + Environment.NewLine +
    "The next step is to manually dump each element with dumpvc <the Element Methodtable> <[item] address>." + Environment.NewLine +
    "> dcq 00000202a7933370" + Environment.NewLine +
    "System.Collections.Concurrent.ConcurrentQueue<ForDump.ValueType>" + Environment.NewLine +
    "   1 - dumparray 202a79334e0" + Environment.NewLine +
    "   2 - dumparray 202a7938a88" + Environment.NewLine +
    Environment.NewLine +
    ""
    ;
    }
}
