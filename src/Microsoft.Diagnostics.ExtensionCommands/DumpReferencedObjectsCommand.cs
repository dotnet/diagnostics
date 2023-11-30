// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "dumpreferencedobjects", Aliases = new[] { "dro", "DumpReferencedObjects" }, Help = "Displays a list of all managed objects that are referenced by an object at given address")]
    public class DumpReferencedObjectsCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public DumpHeapService DumpHeapService { get; set; }

        [Argument(Name = "target")]
        public string TargetAddress { get; set; }

        [Option(Name = "-stat")]
        public bool Stat { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(TargetAddress, out ulong address))
            {
                throw new ArgumentException($"Invalid object address: '{TargetAddress}'", nameof(TargetAddress));
            }

            HashSet<ClrObject> referencedObjects = CalculateReferencedObjects(address);

            IEnumerable<ClrObject> objectsToPrint =
                Runtime.Heap.EnumerateObjects().Where(obj => referencedObjects.Contains(obj));

            DumpHeapService.PrintHeap(objectsToPrint, DumpHeapService.DisplayKind.Normal, Stat, false);
        }

        private HashSet<ClrObject> CalculateReferencedObjects(ulong methodTable)
        {
            ClrObject clrObject = Runtime.Heap.GetObject(methodTable);

            if (clrObject.IsNull)
            {
                throw new ArgumentException($"Method table {methodTable:X} is not valid");
            }

            HashSet<ClrObject> visited = new() { clrObject };
            Queue<ClrObject> toVisit = new(clrObject.EnumerateReferences());

            while (toVisit.Count > 0)
            {
                ClrObject obj = toVisit.Dequeue();
                if (obj.Type is {} && visited.Add(obj))
                {
                    foreach (ClrObject reference in obj.EnumerateReferences())
                    {
                        toVisit.Enqueue(reference);
                    }
                }
            }

            // do not count object itself as referenced?
            visited.Remove(clrObject);

            return visited;
        }
    }
}
