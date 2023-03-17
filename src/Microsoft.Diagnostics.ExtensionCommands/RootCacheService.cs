// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// It is very expensive to enumerate roots, and relatively cheap to store them in memory.
    /// This service is a cache of roots to use instead of calling ClrHeap.EnumerateRoots.
    /// </summary>
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public class RootCacheService
    {
        private List<(ulong Source, ulong Target)> _dependentHandles;
        private ReadOnlyCollection<ClrRoot> _handleRoots;
        private ReadOnlyCollection<ClrRoot> _finalizerRoots;
        private ReadOnlyCollection<ClrRoot> _stackRoots;
        private bool _printedWarning;
        private bool _printedStackWarning;

        [ServiceImport]
        public IConsoleService Console { get; set; }

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        public ReadOnlyCollection<(ulong Source, ulong Target)> GetDependentHandles()
        {
            // This is effectively "InitializeHandleRoots".  This sets _dependentHandles.
            GetHandleRoots();

            // We keep _dependentHandles as a List instead of ReadOnlyCollection so we can use
            // List<>.BinarySearch.
            return _dependentHandles.AsReadOnly();
        }

        public bool IsDependentHandleLink(ulong source, ulong target)
        {
            int i = _dependentHandles.BinarySearch((source, target));
            return i >= 0;
        }

        public IEnumerable<ClrRoot> EnumerateRoots()
        {
            PrintWarning();

            foreach (ClrRoot root in GetHandleRoots())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                yield return root;
            }

            foreach (ClrRoot root in GetFinalizerQueueRoots())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                yield return root;
            }

            // If we made it here without the user breaking out of the enumeration
            // then we've already printed a warning on this command run, we don't
            // need to also print the stack warning.
            _printedStackWarning = true;
            foreach (ClrRoot root in GetStackRoots())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                yield return root;
            }
        }

        public ReadOnlyCollection<ClrRoot> GetHandleRoots()
        {
            InitializeHandleRoots();
            return _handleRoots;
        }


        private void InitializeHandleRoots()
        {
            if (_handleRoots is not null && _dependentHandles is not null)
            {
                return;
            }

            PrintWarning();
            List<(ulong Source, ulong Target)> dependentHandles = new();
            List<ClrRoot> handleRoots = new();

            foreach (ClrHandle handle in Runtime.EnumerateHandles())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (handle.HandleKind == ClrHandleKind.Dependent)
                {
                    dependentHandles.Add((handle.Object, handle.Dependent));
                }

                if (!handle.IsStrong)
                {
                    continue;
                }

                handleRoots.Add(handle);
            }

            // Sort dependentHandles so it can be binary searched
            dependentHandles.Sort();

            _handleRoots = handleRoots.AsReadOnly();
            _dependentHandles = dependentHandles;
        }

        public ReadOnlyCollection<ClrRoot> GetFinalizerQueueRoots()
        {
            if (_finalizerRoots is not null)
            {
                return _finalizerRoots;
            }

            PrintWarning();

            // This should be fast, there's rarely many FQ roots
            _finalizerRoots = Runtime.Heap.EnumerateFinalizerRoots().ToList().AsReadOnly();
            return _finalizerRoots;
        }

        private void PrintWarning()
        {
            if (!_printedWarning)
            {
                Console.WriteLineWarning("Caching GC roots, this may take a while.");
                Console.WriteLineWarning("Subsequent runs of this command will be faster.");
                _printedWarning = true;
            }
        }

        public ReadOnlyCollection<ClrRoot> GetStackRoots()
        {
            if (_stackRoots is not null)
            {
                return _stackRoots;
            }

            // Stack roots can take an extra long time to walk, and one mode of !gcroot skips enumerating stack roots.  If the user
            // calls "!gcroot -nostack" they will get a warning the first time, but if they call it again without "-nostack" they
            // may be surprised by a very long pause.  We skip this second message if the user is calling EnumerateRoots().
            if (!_printedStackWarning)
            {
                Console.WriteLineWarning("Caching stack roots, this may take a while.  Subsequent runs of this command should be faster.");
                _printedStackWarning = true;
            }

            List<ClrRoot> stackRoots = new();
            foreach (ClrThread thread in Runtime.Threads.Where(thread => thread.IsAlive))
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                foreach (ClrRoot root in thread.EnumerateStackRoots())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    stackRoots.Add(root);
                }
            }

            _stackRoots = stackRoots.AsReadOnly();
            return _stackRoots;
        }
    }
}
