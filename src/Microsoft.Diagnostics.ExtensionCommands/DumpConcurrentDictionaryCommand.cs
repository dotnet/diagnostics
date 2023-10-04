// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpconcurrentdictionary", Aliases = new string[] { "dcd" }, Help = "Displays concurrent dictionary content.")]
    public class DumpConcurrentDictionaryCommand : ClrMDHelperCommandBase
    {
        [Argument(Help = "The address of a ConcurrentDictionary object.")]
        public string Address { get; set; }

        [ServiceImport(Optional = true)]
        public ClrRuntime Runtime { get; set; }

        public override void Invoke()
        {
            if (string.IsNullOrEmpty(Address))
            {
                throw new DiagnosticsException("Missing ConcurrentDictionary address...");
            }

            if (!TryParseAddress(Address, out ulong address))
            {
                throw new DiagnosticsException("Hexadecimal address expected...");
            }

            ClrHeap heap = Runtime.Heap;
            ClrType type = heap.GetObjectType(address);
            if (type?.Name is null)
            {
                throw new DiagnosticsException($"{Address:x16} is not referencing an object...");
            }

            if (!type.Name.StartsWith("System.Collections.Concurrent.ConcurrentDictionary<"))
            {
                throw new DiagnosticsException($"{Address:x16} is not a ConcurrentDictionary but an instance of {type.Name}...");
            }

            WriteLine($"{type.Name}");
            try
            {
                int count = 0;
                foreach (KeyValuePair<string, string> item in Helper.EnumerateConcurrentDictionary(address))
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

        [HelpInvoke]
        public static string GetDetailedHelp() =>
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
