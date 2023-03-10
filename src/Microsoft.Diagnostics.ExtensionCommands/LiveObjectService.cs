using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class LiveObjectService
    {
        private ObjectSet _liveObjs = null;

        public bool PrintWarning { get; set; } = true;

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public IConsoleService Console { get; set; }


        public bool IsObjectLive(ulong obj)
        {
            _liveObjs ??= CreateObjectSet();
            return _liveObjs.Contains(obj);
        }

        private ObjectSet CreateObjectSet()
        {
            Stopwatch sw = Stopwatch.StartNew();

            bool printWarning = PrintWarning;
            if (printWarning)
                Console.Write("Calculating live objects, this may take a while...");

            ClrHeap heap = Runtime.Heap;
            ObjectSet result = new(heap);

            Queue<ulong> todo = new();

            foreach (ulong obj in heap.EnumerateRoots().Select(r => r.Object.Address).Distinct())
                todo.Enqueue(obj);

            while (todo.Count > 0)
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                ulong currAddress = todo.Dequeue();
                ClrObject obj = heap.GetObject(currAddress);
                if (!result.Add(obj))
                    continue;

                foreach (ulong address in obj.EnumerateReferenceAddresses(carefully: false, considerDependantHandles: true))
                    if (!result.Contains(address))
                        todo.Enqueue(address);
            }

            if (printWarning)
                Console.WriteLine($"completed in {sw.Elapsed}.");

            return result;
        }
    }
}
