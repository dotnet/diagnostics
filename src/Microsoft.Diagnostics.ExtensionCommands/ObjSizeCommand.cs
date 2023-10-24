// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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

        public override void Invoke()
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

            Console.Write($"Objects which ");
            Console.WriteDmlExec(obj.Address.ToString("x"), $"!dumpobj {obj.Address:x}");
            Console.WriteLine($"({obj.Type?.Name ?? " <unknown type>"}) transitively keep alive:");
            Console.WriteLine();

            DumpHeapService.DisplayKind displayKind = Strings ? DumpHeapService.DisplayKind.Strings : DumpHeapService.DisplayKind.Normal;
            DumpHeap.PrintHeap(GetTransitiveClosure(obj), displayKind, Stat, printFragmentation: false);
        }

        private static IEnumerable<ClrObject> GetTransitiveClosure(ClrObject obj)
        {
            HashSet<ulong> seen = new() { obj };
            Queue<ClrObject> queue = new();

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
