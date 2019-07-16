// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    public class MemoryDump : IDisposable
    {
        public DataTarget Target { get; }
        public ClrRuntime Runtime { get; }
        public int ActiveThreadId { get; set; }
        public ClrThread ActiveThread => GetThread(ActiveThreadId);

        public MemoryDump(DataTarget target, ClrRuntime runtime)
        {
            Target = target;
            Runtime = runtime;

            var firstThread = runtime.Threads.FirstOrDefault();
            if (firstThread != null)
            {
                ActiveThreadId = firstThread.ManagedThreadId;
            }
        }

        public ClrThread GetThread(int id)
        {
            return Runtime.Threads.FirstOrDefault(t => t.ManagedThreadId == id);
        }

        public IList<TypeHeapStats> ComputeHeapStatistics()
        {
            // Compute heap information
            var stats = new Dictionary<string, TypeHeapStats>();
            foreach (var obj in Runtime.Heap.EnumerateObjects())
            {
                var type = obj.Type.Name;
                if (!stats.TryGetValue(type, out var heapStats))
                {
                    heapStats = new TypeHeapStats(obj.Type);
                    stats[type] = heapStats;
                }

                heapStats.AddObject(obj.Size);
            }

            return stats.Values.ToList();
        }

        public void Dispose()
        {
            Target.Dispose();
        }
    }
}
