// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class LiveObjectService
    {
        private ObjectSet _liveObjs;

        public int UpdateSeconds { get; set; } = 15;

        public bool PrintWarning { get; set; } = true;

        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IConsoleService Console { get; set; }

        public bool IsLive(ClrObject obj) => IsLive(obj.Address);

        public bool IsLive(ulong obj)
        {
            _liveObjs ??= CreateObjectSet();
            return _liveObjs.Contains(obj);
        }

        private ObjectSet CreateObjectSet()
        {
            ClrHeap heap = Runtime.Heap;
            ObjectSet live = new(heap);

            Stopwatch sw = Stopwatch.StartNew();
            int updateSeconds = Math.Max(UpdateSeconds, 10);
            bool printWarning = PrintWarning;

            if (printWarning)
            {
                Console.WriteLine("Calculating live objects, this may take a while...");
            }

            int roots = 0;
            Queue<ulong> todo = new();
            foreach (ClrRoot root in RootCache.EnumerateRoots())
            {
                roots++;
                if (printWarning && sw.Elapsed.TotalSeconds > updateSeconds && live.Count > 0)
                {
                    Console.WriteLine($"Calculating live objects: {live.Count:n0} found");
                    sw.Restart();
                }

                if (live.Add(root.Object))
                {
                    todo.Enqueue(root.Object);
                }
            }

            // We calculate the % complete based on how many are left in our todo queue.
            // This means that % complete can go down if we end up seeing an unexpectedly
            // high number of references compared to earlier objects.
            int maxCount = todo.Count;
            while (todo.Count > 0)
            {
                if (printWarning && sw.Elapsed.TotalSeconds > updateSeconds)
                {
                    if (todo.Count > maxCount)
                    {
                        Console.WriteLine($"Calculating live objects: {live.Count:n0} found");
                    }
                    else
                    {
                        Console.WriteLine($"Calculating live objects: {live.Count:n0} found - {(maxCount - todo.Count) * 100 / (float)maxCount:0.0}% complete");
                    }

                    maxCount = Math.Max(maxCount, todo.Count);
                    sw.Restart();
                }

                Console.CancellationToken.ThrowIfCancellationRequested();

                ulong currAddress = todo.Dequeue();
                ClrObject obj = heap.GetObject(currAddress);

                foreach (ulong address in obj.EnumerateReferenceAddresses(carefully: false, considerDependantHandles: true))
                {
                    if (live.Add(address))
                    {
                        todo.Enqueue(address);
                    }
                }
            }

            if (printWarning)
            {
                Console.WriteLine($"Calculating live objects complete: {live.Count:n0} objects from {roots:n0} roots");
            }

            return live;
        }
    }
}
