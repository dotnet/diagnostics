// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpconcurrentdictionary", Aliases = new string[] { "dcd" }, Help = "Displays concurrent dictionary content.")]
    public class DumpConcurrentDictionaryCommand : ExtensionCommandBase
    {
        [Argument(Help = "The address of a ConcurrentDictionary object.")]
        public string Address { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public override void ExtensionInvoke()
        {
            if (string.IsNullOrEmpty(Address))
            {
                WriteLine("Missing ConcurrentDictionary address...");
                return;
            }

            if (!TryParseAddress(Address, out var address))
            {
                WriteLine("Hexadecimal address expected...");
                return;
            }

            var heap = Runtime.Heap;
            var type = heap.GetObjectType(address);
            if (type?.Name is null)
            {
                WriteLine($"{Address:x16} is not referencing an object...");
                return;
            }

            if (!type.Name.StartsWith("System.Collections.Concurrent.ConcurrentDictionary<"))
            {
                WriteLine($"{Address:x16} is not a ConcurrentDictionary but an instance of {type.Name}...");
                return;
            }

            WriteLine($"{type.Name}");
            try
            {
                var count = 0;
                foreach (var item in Helper.EnumerateConcurrentDictionary(address))
                {
                    count++;
                    WriteLine($"    -----");
                    WriteLine($"      Key: {Truncate(item.Key, 100)}");
                    WriteLine($"    Value: {Truncate(item.Value, 100)}");
                }
                WriteLine("---------------------------------------------");
                WriteLine($"{count} items");
            }
            catch (Exception x)
            {
                WriteLine(x.Message);
            }

            WriteLine(string.Empty);
        }

        protected override string GetDetailedHelp()
        {
            return
@"-------------------------------------------------------------------------------
DumpConcurrentDictionary
Lists all items (key/value pairs) in the given concurrent dictionary.

> dcd 00000184aa23e2e0
System.Collections.Concurrent.ConcurrentDictionary<System.Int32, ForDump.DumpStruct>
    -----
      Key: 31
    Value: dumpvc 0x00007ff9ea6e2778 0x00000184aa241e88
    -----
      Key: 1521482
    Value: dumpvc 0x00007ff9ea6e2778 0x00000184aa241c48
---------------------------------------------
2 items

- For simple types such as numbers, boolean and string, values are shown.
- In case of reference types, the command to dump each object is shown (e.g. dumpobj <[item] address>).
- For value types, the command to dump each value type is shown (e.g. dumpvc <the Element Methodtable> <[item] address>).
";
        }

        private static string Truncate(string str, int nbMaxChars)
        {
            if (str.Length <= nbMaxChars)
            {
                return str;
            }

            return str.Substring(0, nbMaxChars) + "...";
        }
    }
}
