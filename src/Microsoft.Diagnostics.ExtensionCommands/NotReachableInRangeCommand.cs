// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// Prints objects and statistics for a range of object pointers.
    /// </summary>
    [Command(Name = "notreachableinrange", Help = "A helper command for !finalizerqueue")]
    public class NotReachableInRangeCommand : CommandBase
    {
        private HashSet<ulong> _nonFQLiveObjects;

        [ServiceImport]
        public LiveObjectService LiveObjects { get; set; }

        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [ServiceImport]
        public DumpHeapService DumpHeap { get; set; }

        [ServiceImport]
        public IMemoryService Memory { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [Option(Name = "-short")]
        public bool Short { get; set; }

        [Option(Name = "-nofinalizer")]
        public bool NoFinalizer { get; set; }

        [Argument(Name = "start")]
        public string StartAddress { get; set; }

        [Argument(Name = "end")]
        public string EndAddress { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(StartAddress, out ulong start))
            {
                throw new ArgumentException($"Could not parse start address: {StartAddress}");
            }

            if (!TryParseAddress(EndAddress, out ulong end))
            {
                throw new ArgumentException($"Could not parse end address: {EndAddress}");
            }

            // pointer align
            start &= ~(((ulong)Memory.PointerSize) - 1);
            ulong curr = start;

            IEnumerable<ClrObject> liveObjs = EnumerateLiveObjectsInRange(end, curr);
            DumpHeap.PrintHeap(liveObjs, Short ? DumpHeapService.DisplayKind.Short : DumpHeapService.DisplayKind.Normal, statsOnly: false, printFragmentation: false);
        }

        private IEnumerable<ClrObject> EnumerateLiveObjectsInRange(ulong end, ulong curr)
        {
            while (curr < end)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (!Memory.ReadPointer(curr, out ulong objAddress))
                {
                    throw new IOException($"Could not read pointer {curr:x}");
                }

                ClrObject obj = Runtime.Heap.GetObject(objAddress);
                if (!obj.IsValid)
                {
                    Console.WriteLineWarning($"Warning: {objAddress:x} is not a valid object");
                }

                if (IsDead(obj))
                {
                    yield return obj;
                }

                curr += (uint)Memory.PointerSize;
            }
        }

        private bool IsDead(ClrObject obj)
        {
            if (NoFinalizer)
            {
                _nonFQLiveObjects ??= GetNonFQLiveObjects();
                return !_nonFQLiveObjects.Contains(obj);
            }
            else
            {
                return !LiveObjects.IsLive(obj);
            }
        }

        private HashSet<ulong> GetNonFQLiveObjects()
        {
            // It's really rare to need non-FQ roots (just !finalizerqueue).  We'll calculate this on
            // demand instead of caching it and possibly holding too much memory for something we'll
            // never need again.
            HashSet<ulong> live = new();
            Queue<ulong> todo = new();
            IEnumerable<ClrRoot> nonFQRoots = RootCache.GetStackRoots().Concat(RootCache.GetHandleRoots());
            foreach (ClrRoot root in nonFQRoots)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                if (live.Add(root.Object))
                {
                    todo.Enqueue(root.Object);
                }
            }

            while (todo.Count > 0)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                ulong currAddress = todo.Dequeue();
                ClrObject obj = Runtime.Heap.GetObject(currAddress);

                foreach (ulong address in obj.EnumerateReferenceAddresses(carefully: false, considerDependantHandles: true))
                {
                    if (live.Add(address))
                    {
                        todo.Enqueue(address);
                    }
                }
            }

            return live;
        }
    }
}
