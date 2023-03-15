// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name ="pathto", Aliases = new[] { "PathTo" }, Help = "Displays the GC path from <root> to <target>.")]
    public class PathToCommand : ClrRuntimeCommandBase
    {
        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [Argument(Name = "source")]
        public string SourceAddress { get; set; }

        [Argument(Name = "target")]
        public string TargetAddress { get; set; }

        public override void ExtensionInvoke()
        {
            if (TryParseAddress(SourceAddress, out ulong source))
            {
                throw new ArgumentException($"Could not parse argument 'source': {source}");
            }

            if (TryParseAddress(TargetAddress, out ulong target))
            {
                throw new ArgumentException($"Could not parse argument 'source': {target}");
            }

            ClrHeap heap = Runtime.Heap;
            GCRoot gcroot = new(heap, (found) =>
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                return found == target;
            });

            ClrObject sourceObj = heap.GetObject(source);
            if (!sourceObj.IsValid)
            {
                Console.WriteLine($"Source address {source:x} is not a valid object.");
                return;
            }

            ClrObject targetObj = heap.GetObject(target);
            if (!sourceObj.IsValid)
            {
                Console.WriteLine($"Warning: Target address {target:x} is not a valid object.");
                return;
            }

            GCRoot.ChainLink path = gcroot.FindPathFrom(sourceObj);
            if (path is not null)
            {
                GCRootCommand.PrintPath(Console, RootCache, null, heap, path);
            }
            else
            {
                Console.WriteLine($"Could not find a path from {source:x} to {target:x}");
            }
        }
    }
}
