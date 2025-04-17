// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "objsize", Aliases = new[] { "ObjSize" }, Help = "Lists the sizes of the all the objects found on managed threads.")]
    public class ObjSizeCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public DumpHeapService DumpHeap { get; set; }

        [Option(Name = "-short")]
        public bool Short { get; set; }

        [Option(Name = "-strings")]
        public bool Strings { get; set; }

        [Option(Name = "-stat", Aliases = new string[] { "-summary" })]
        public bool Stat { get; set; }

        [Argument]
        public string ObjectAddress { get; set; }

        [Option(Name = "-mt")]
        public string MethodTable { get; set; }

        public override void Invoke()
        {
            IEnumerable<ClrObject> objects;

            if (!string.IsNullOrEmpty(MethodTable))
            {
                if (!TryParseAddress(MethodTable, out ulong methodTable))
                {
                    throw new ArgumentException($"Could not parse method table: {MethodTable}");
                }

                objects = Runtime.Heap.EnumerateObjects().Where(obj => obj.Type?.MethodTable == methodTable);

                Console.Write("Objects which ");
                Console.WriteLine($"({Runtime.Heap.GetTypeByMethodTable(methodTable)?.Name ?? "<unknown type>"}) transitively keep alive:");
                Console.WriteLine();
            }
            else
            {
                if (!TryParseAddress(ObjectAddress, out ulong objAddress))
                {
                    throw new ArgumentException($"Could not parse target object address: {objAddress:x}");
                }

                ClrObject obj = Runtime.Heap.GetObject(objAddress);
                if (!obj.IsValid)
                {
                    Console.WriteLine($"{objAddress:x} is not a valid object");
                    return;
                }

                objects = new[] { obj };

                Console.Write("Objects which ");
                Console.WriteDmlExec(obj.Address.ToString("x"), $"!dumpobj {obj.Address:x}");
                Console.WriteLine($" ({obj.Type?.Name ?? "<unknown type>"}) transitively keep alive:");
                Console.WriteLine();
            }

            DumpHeapService.DisplayKind displayKind;
            if (Strings)
            {
                displayKind = DumpHeapService.DisplayKind.Strings;
            }
            else if (Short)
            {
                displayKind = DumpHeapService.DisplayKind.Short;
            }
            else
            {
                displayKind = DumpHeapService.DisplayKind.Normal;
            }

            DumpHeap.PrintHeap(GetTransitiveClosure(objects), displayKind, Stat, printFragmentation: false);
        }

        [HelpInvoke]
        public static string GetDetailedHelp() =>
@"With no parameters, 'objsize' lists the size of all objects found on managed 
threads. It also enumerates all GCHandles in the process, and totals the size 
of any objects pointed to by those handles. In calculating object size, 
!ObjSize includes the size of all child objects in addition to the parent.

For example, 'dumpobj' lists a size of 20 bytes for this Customer object:

    {prompt}dumpobj a79d40
    Name: Customer
    MethodTable: 009038ec
    EEClass: 03ee1b84
    Size: 20(0x14) bytes
     (C:\pub\unittest.exe)
    Fields:
          MT    Field   Offset                 Type       Attr    Value Name
    009038ec  4000008        4                CLASS   instance 00a79ce4 name
    009038ec  4000009        8                CLASS   instance 00a79d2c bank
    009038ec  400000a        c       System.Boolean   instance        1 valid

but 'objsize' lists 152 bytes:

    {prompt}objsize a79d40
    sizeof(00a79d40) =      152 (    0x98) bytes (Customer)

This is because a Customer points to a Bank, has a name, and the Bank points to
an Address string. You can use !ObjSize to identify any particularly large 
objects, such as a managed cache in a web server.

While running ObjSize with no arguments may point to specific roots that hold 
onto large amounts of memory it does not provide information regarding the 
amount of managed memory that is still alive.  This is due to the fact that a 
number of roots can share a common subgraph, and that part will be reported in 
the size of all the roots that reference the subgraph.
";
        private static IEnumerable<ClrObject> GetTransitiveClosure(IEnumerable<ClrObject> objects)
        {
            HashSet<ulong> seen = new();
            Queue<ClrObject> queue = new();

            foreach (ClrObject obj in objects)
            {
                if (obj.IsValid && seen.Add(obj))
                {
                    queue.Enqueue(obj);

                    while (queue.Count > 0)
                    {
                        ClrObject parent = queue.Dequeue();
                        yield return parent;

                        foreach (ClrObject child in parent.EnumerateReferences())
                        {
                            if (child.IsValid && seen.Add(child))
                            {
                                queue.Enqueue(child);
                            }
                        }
                    }
                }
            }
        }
    }
}
