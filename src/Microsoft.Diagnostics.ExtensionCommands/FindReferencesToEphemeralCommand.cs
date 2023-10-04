// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "ephrefs", Help = "Finds older generation objects which reference objects in the ephemeral segment.")]
    public class FindReferencesToEphemeralCommand : ClrRuntimeCommandBase
    {
        private readonly HashSet<ulong> _referenced = new();
        private ulong _referencedSize;

        public override void Invoke()
        {
            Table output = new(Console, DumpObj, DumpHeap, ByteCount, Column.ForEnum<Generation>(), Column.ForEnum<Generation>(), ByteCount, Integer, TypeName);

            var generationGroup = from item in FindObjectsWithEphemeralReferences()
                                  group item by (item.ObjectGeneration, item.ReferenceGeneration) into g
                                  select new
                                  {
                                      g.Key.ObjectGeneration,
                                      g.Key.ReferenceGeneration,
                                      Objects = g.OrderBy(x => x.Object.Address),
                                  };

            long objCount = 0;

            (Generation, Generation) last = default;
            foreach (var item in generationGroup)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                string objGen = item.ObjectGeneration.ToString().ToLowerInvariant();
                string refGen = item.ReferenceGeneration.ToString().ToLowerInvariant();

                // Print a new header every time we hit a different combination of object/reference generations
                (Generation, Generation) curr = (item.ObjectGeneration, item.ReferenceGeneration);
                if (last != curr)
                {
                    if (last != default)
                    {
                        Console.WriteLine();
                    }

                    last = curr;

                    Console.WriteLine($"References from {objGen} to {refGen}:");
                    Console.WriteLine();
                    output.WriteHeader("Object", "MethodTable", "Size", "Obj Gen", "Ref Gen", "Obj Count", "Obj Size", "Type");
                }

                foreach (EphemeralRefCount erc in item.Objects)
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    objCount++;
                    output.WriteRow(erc.Object, erc.Object.Type, erc.Object.Size, erc.ObjectGeneration, erc.ReferenceGeneration, erc.Count, erc.Size, erc.Object.Type);
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{objCount:n0} older generation objects referenced {_referenced.Count:n0} younger objects ({_referencedSize:n0} bytes)");
        }

        private IEnumerable<EphemeralRefCount> FindObjectsWithEphemeralReferences()
        {
            foreach (ClrSegment seg in Runtime.Heap.Segments)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                // Only skip Gen0 and Frozen regions entirely
                if (seg.Kind is GCSegmentKind.Generation0 or GCSegmentKind.Frozen)
                {
                    continue;
                }

                foreach (ClrObject obj in seg.EnumerateObjects().Where(obj => obj.IsValid && obj.ContainsPointers))
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();

                    // Skip this object if it's gen0 or we hit an error
                    Generation objGen = seg.GetGeneration(obj);
                    if (objGen is Generation.Generation0 or Generation.Unknown)
                    {
                        continue;
                    }

                    // Keep track of whether we've enumerated Gen0/Gen1 references already
                    EphemeralRefCount gen0 = null;
                    EphemeralRefCount gen1 = null;
                    foreach (ClrObject objRef in obj.EnumerateReferences(considerDependantHandles: false))
                    {
                        Console.CancellationToken.ThrowIfCancellationRequested();

                        if (!objRef.IsValid || objRef.IsFree)
                        {
                            continue;
                        }

                        ulong refObjSize = objRef.Size;

                        ClrSegment refSeg = Runtime.Heap.GetSegmentByAddress(objRef);
                        Generation refGen = refSeg?.GetGeneration(objRef) ?? Generation.Unknown;
                        switch (refGen)
                        {
                            case Generation.Generation0:
                                gen0 ??= new EphemeralRefCount()
                                {
                                    Object = obj,
                                    ObjectGeneration = objGen,
                                    ReferenceGeneration = refGen,
                                };

                                gen0.Count++;
                                gen0.Size += refObjSize;
                                if (_referenced.Add(objRef))
                                {
                                    _referencedSize += refObjSize;
                                }

                                break;

                            case Generation.Generation1:
                                if (objGen > Generation.Generation1)
                                {
                                    gen1 ??= new EphemeralRefCount()
                                    {
                                        Object = obj,
                                        ObjectGeneration = objGen,
                                        ReferenceGeneration = refGen,
                                    };

                                    gen1.Count++;
                                    gen1.Size += refObjSize;
                                    if (_referenced.Add(objRef))
                                    {
                                        _referencedSize += refObjSize;
                                    }
                                }
                                break;
                        }
                    }

                    if (gen0 is not null)
                    {
                        yield return gen0;
                    }

                    if (gen1 is not null)
                    {
                        yield return gen1;
                    }
                }
            }
        }

        private sealed class EphemeralRefCount
        {
            public ClrObject Object { get; set; }
            public Generation ObjectGeneration { get; set; }
            public Generation ReferenceGeneration { get; set; }
            public int Count { get; set; }
            public ulong Size { get; set; }
        }
    }
}
