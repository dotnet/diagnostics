// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.ExtensionCommands.Output;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.Output.ColumnKind;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "gcroot", Aliases = new[] { "GCRoot" }, Help = "Displays info about references (or roots) to an object at the specified address.")]
    public class GCRootCommand : ClrRuntimeCommandBase
    {
        private StringBuilder _lineBuilder = new(64);
        private ClrRoot _lastRoot;

        [ServiceImport]
        public IMemoryService Memory { get; set; }

        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [ServiceImport]
        public StaticVariableService StaticVariables { get; set; }

        [ServiceImport]
        public ManagedFileLineService FileLineService { get; set; }

        [Option(Name = "-gcgen", Help = "Implementation helper for !findroots.")]
        public int? AsGCGeneration { get; set; }

        [Option(Name="-nostacks", Help ="Do not use stack roots.")]
        public bool NoStacks { get; set; }

        [Argument(Name = "target")]
        public string TargetAddress { get; set; }

        [Option(Name = "-limit", Help = "Limits the amount of roots to find")]
        public int? Limit { get; set; }

        public override void Invoke()
        {
            if (!TryParseAddress(TargetAddress, out ulong address))
            {
                throw new ArgumentException($"Could not parse target object address: {TargetAddress:x}");
            }

            ClrObject obj = Runtime.Heap.GetObject(address);
            if (!obj.IsValid)
            {
                Console.WriteWarning($"Warning: {address:x} is not a valid object");
            }

            GCRoot gcroot = new(Runtime.Heap, (found) =>
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                return found == address;
            });

            int count;
            int limit = Limit ?? int.MaxValue;

            if (AsGCGeneration.HasValue)
            {
                int gen = AsGCGeneration.Value;

                ClrSegment seg = Runtime.Heap.GetSegmentByAddress(address);
                if (seg is null)
                {
                    throw new DiagnosticsException($"Address {address:x} is not in the managed heap.");
                }

                Generation objectGen = seg.GetGeneration(address);
                if (gen < (int)objectGen)
                {
                    Console.WriteLine($"Object {address:x} will survive this collection:");
                    Console.WriteLine($"    gen({address:x}) = {objectGen} > {gen} = condemned generation.");
                    return;
                }

                if (gen < 0 || gen > 1)
                {
                    // If not gen0 or gen1, treat it as a normal !gcroot
                    if (NoStacks)
                    {
                        count = PrintNonStackRoots(gcroot, limit);
                    }
                    else
                    {
                        count = PrintAllRoots(gcroot, limit);
                    }
                }
                else
                {
                    count = PrintOlderGenerationRoots(gcroot, gen, limit);
                    count += PrintNonStackRoots(gcroot, limit);
                }
            }
            else if (NoStacks)
            {
                count = PrintNonStackRoots(gcroot, limit);
            }
            else
            {
                count = PrintAllRoots(gcroot, limit);
            }

            Console.WriteLine($"Found {count:n0} unique roots.");
        }

        private int PrintOlderGenerationRoots(GCRoot gcroot, int gen, int limit)
        {
            int count = 0;

            bool noInternalRootData = true;
            foreach (ClrSubHeap subheap in Runtime.Heap.SubHeaps)
            {
                MemoryRange internalRootArray = subheap.InternalRootArray;
                if (internalRootArray.Length == 0)
                {
                    continue;
                }

                noInternalRootData = false;

                bool first = true;
                ulong address = internalRootArray.Start;
                while (internalRootArray.Contains(address))
                {
                    if (count >= limit)
                    {
                        break;
                    }

                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (Memory.ReadPointer(address, out ulong objAddress))
                    {
                        ClrObject obj = Runtime.Heap.GetObject(objAddress);
                        if (obj.IsValid)
                        {
                            GCRoot.ChainLink path = gcroot.FindPathFrom(obj);
                            if (path is not null)
                            {
                                if (first)
                                {
                                    Console.WriteLine("Older Generation:");
                                    first = false;
                                }

                                Console.WriteLine($"    {objAddress:x}");
                                PrintPath(Console, RootCache, StaticVariables, Runtime.Heap, path);
                                Console.WriteLine();

                                count++;
                            }
                        }
                        else
                        {
                            Console.WriteLineWarning($"Warning: GC internal root array contained invalid object: *{address:x} = {objAddress:x}");
                        }
                    }

                    address += (uint)Memory.PointerSize;
                }
            }

            if (noInternalRootData)
            {
                throw new InvalidDataException("Could not gather needed data, possibly due to memory constraints in the debuggee.\n" +
                                              $"To try again, re-issue the '!findroots -gen {gen}' command.");
            }

            return count;
        }

        private int PrintAllRoots(GCRoot gcroot, int limit)
        {
            int count = 0;
            foreach (ClrRoot root in RootCache.EnumerateRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                Console.CancellationToken.ThrowIfCancellationRequested();
                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item is not null)
                {
                    PrintPath(root, item);
                    count++;
                }
            }

            return count;
        }

        private int PrintNonStackRoots(GCRoot gcroot, int limit)
        {
            int count = 0;
            foreach (ClrRoot root in RootCache.GetHandleRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                Console.CancellationToken.ThrowIfCancellationRequested();
                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item is not null)
                {
                    PrintPath(root, item);
                    count++;
                }
            }

            foreach (ClrRoot root in RootCache.GetFinalizerQueueRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                Console.CancellationToken.ThrowIfCancellationRequested();
                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item is not null)
                {
                    PrintPath(root, item);
                    count++;
                }
            }

            return count;
        }

        private void PrintPath(ClrRoot root, GCRoot.ChainLink link)
        {
            PrintRoot(root);
            PrintPath(Console, RootCache, StaticVariables, Runtime.Heap, link);
            Console.WriteLine();
        }

        public static void PrintPath(IConsoleService console, RootCacheService rootCache, StaticVariableService statics, ClrHeap heap, GCRoot.ChainLink link)
        {
            Table objectOutput = new(console, Text.WithWidth(2), DumpObj, TypeName, Text)
            {
                Indent = new(' ', 10)
            };

            objectOutput.SetAlignment(Align.Left);

            bool first = true;
            bool isPossibleStatic = true;

            ClrObject firstObj = default;

            ulong prevObj = 0;
            while (link != null)
            {
                ClrObject obj = heap.GetObject(link.Object);

                // Check whether this link is a dependent handle
                string extraText = "";
                bool isDependentHandleLink = rootCache.IsDependentHandleLink(prevObj, link.Object);
                if (isDependentHandleLink)
                {
                    extraText = "(dependent handle)";
                }

                // Print static variable info.  In all versions of the runtime, static variables are stored in
                // a pinned object array.  We check if the first link in the chain is an object[], and if so we
                // check if the second object's address is the location of a static variable.  We could further
                // narrow this by checking the root type, but that needlessly complicates this code...we can't
                // get false positives or negatives here (as nothing points to static variable object[] other
                // than the root).
                if (first)
                {
                    firstObj = obj;
                    isPossibleStatic = firstObj.IsValid && firstObj.IsArray && firstObj.Type.Name == "System.Object[]";
                    first = false;
                }
                else if (isPossibleStatic)
                {
                    if (statics is not null && !isDependentHandleLink)
                    {
                        foreach (ClrReference reference in firstObj.EnumerateReferencesWithFields(carefully: false, considerDependantHandles: false))
                        {
                            if (reference.Object == obj)
                            {
                                ulong address = firstObj + (uint)reference.Offset;

                                if (statics.TryGetStaticByAddress(address, out ClrStaticField field))
                                {
                                    extraText = $"(static variable: {field.Type?.Name ?? "Unknown"}.{field.Name})";
                                    break;
                                }
                            }
                        }
                    }

                    // only the first object[] in the chain is possible to be the static array
                    isPossibleStatic = false;
                }

                objectOutput.WriteRow("->", obj, obj.Type, extraText);

                prevObj = link.Object;
                link = link.Next;
            }
        }

        private void PrintRoot(ClrRoot root)
        {
            if (root is ClrStackRoot stackRoot)
            {
                ClrStackRoot lastStackRoot = _lastRoot as ClrStackRoot;

                ClrThread currThread = stackRoot.StackFrame?.Thread;
                if (currThread is not null && lastStackRoot?.StackFrame?.Thread != currThread)
                {
                    Console.WriteLine($"Thread {currThread.OSThreadId:x}:");
                }

                ClrStackFrame currFrame = stackRoot.StackFrame;
                if (currFrame is not null && lastStackRoot?.StackFrame != currFrame)
                {
                    Console.WriteLine(GetFrameOutput(currFrame));
                }

                Console.WriteLine(GetRegisterOutput(stackRoot));
            }
            else if (root.RootKind == ClrRootKind.FinalizerQueue)
            {
                if (_lastRoot is null || _lastRoot.RootKind != ClrRootKind.FinalizerQueue)
                {
                    Console.WriteLine("Finalizer Queue:");
                }

                Console.WriteLine($"    {root.Address:x16} (finalizer root)");
            }
            else if (root is ClrHandle handle)
            {
                if (_lastRoot is null or not ClrHandle)
                {
                    Console.WriteLine("HandleTable:");
                }

                _lineBuilder.Clear();
                _lineBuilder.Append("    ");
                _lineBuilder.Append(root.Address.ToString("x16"));
                _lineBuilder.Append(" (");
                _lineBuilder.Append(NameForHandle(handle.HandleKind));

                if (handle.HandleKind == ClrHandleKind.RefCounted)
                {
                    _lineBuilder.Append(' ');
                    _lineBuilder.Append("RefCount: ");
                    _lineBuilder.Append(handle.ReferenceCount.ToString("n0"));
                }

                _lineBuilder.Append(')');
                Console.WriteLine(_lineBuilder.ToString());
            }
            else
            {
                // There are no other options, but futureproofing in case we add something new
                if (_lastRoot is null || _lastRoot.RootKind != root.RootKind)
                {
                    Console.WriteLine($"{root.RootKind}:");
                }

                Console.WriteLine($"    {root.Address:x16}");
            }

            _lastRoot = root;
        }

        private static string NameForHandle(ClrHandleKind handleKind)
        {
            return handleKind switch
            {
                ClrHandleKind.WeakShort => "weak short handle",
                ClrHandleKind.WeakLong => "weak long handle",
                ClrHandleKind.Strong => "strong handle",
                ClrHandleKind.Pinned => "pinned handle",
                ClrHandleKind.RefCounted => "ref counted handle",
                ClrHandleKind.Dependent => "dependent handle",
                ClrHandleKind.AsyncPinned => "async pinned handle",
                ClrHandleKind.SizedRef => "sized ref handle",
                ClrHandleKind.WeakWinRT => "weak WinRT handle",
                _ => handleKind.ToString()
            };
        }

        private string GetFrameOutput(ClrStackFrame currFrame)
        {
            _lineBuilder.Clear();
            _lineBuilder.Append("    ");

            _lineBuilder.Append(currFrame.StackPointer.ToString("x"));

            // InstructionPointer is 0 for coreclr!Frame objects.
            if (currFrame.InstructionPointer != 0)
            {
                _lineBuilder.Append(' ');
                _lineBuilder.Append(currFrame.InstructionPointer.ToString("x"));
            }

            if (currFrame.FrameName is not null)
            {
                _lineBuilder.Append(' ');
                _lineBuilder.Append('[');
                _lineBuilder.Append(currFrame.FrameName);
                _lineBuilder.Append("] ");
            }

            if (currFrame.Method is not null)
            {
                _lineBuilder.Append(' ');

                if (currFrame.FrameName is not null)
                {
                    _lineBuilder.Append('(');
                }

                if (currFrame.Method.Signature is not null)
                {
                    _lineBuilder.Append(currFrame.Method.Signature);
                }
                else
                {
                    if (currFrame.Method.Type?.Name is not null)
                    {
                        _lineBuilder.Append(currFrame.Method.Type.Name);
                        _lineBuilder.Append('.');
                    }
                    else
                    {
                        _lineBuilder.Append("UnknownType.");
                    }

                    if (currFrame.Method.Name is not null)
                    {
                        _lineBuilder.Append(currFrame.Method.Name);
                        _lineBuilder.Append("(...)");
                    }
                    else
                    {
                        _lineBuilder.Append("UnknownMethod(...)");
                    }
                }

                if (currFrame.FrameName is not null)
                {
                    _lineBuilder.Append(')');
                }

                (string source, int line) = FileLineService.GetSourceFromManagedMethod(currFrame.Method, currFrame.InstructionPointer);

                if (source is not null)
                {
                    _lineBuilder.Append(" [");
                    _lineBuilder.Append(source);
                    _lineBuilder.Append(" @ ");
                    _lineBuilder.Append(line);
                    _lineBuilder.Append(']');
                }
            }

            return _lineBuilder.ToString();
        }

        private string GetRegisterOutput(ClrStackRoot stackRoot)
        {
            _lineBuilder.Clear();
            _lineBuilder.Append("        ");
            if (stackRoot.RegisterName is not null || stackRoot.RegisterOffset != 0)
            {
                _lineBuilder.Append(stackRoot.RegisterName ?? "???");
                if (stackRoot.RegisterOffset > 0)
                {
                    _lineBuilder.Append('+');
                    _lineBuilder.Append(stackRoot.RegisterOffset.ToString("x"));
                }
                else if (stackRoot.RegisterOffset < 0)
                {
                    _lineBuilder.Append('-');
                    _lineBuilder.Append(Math.Abs(stackRoot.RegisterOffset).ToString("x"));
                }

                _lineBuilder.Append(':');
            }

            if (stackRoot.Address != 0)
            {
                _lineBuilder.Append(' ');
                _lineBuilder.Append(stackRoot.Address.ToString("x16"));
            }

            return _lineBuilder.ToString();
        }
    }
}
