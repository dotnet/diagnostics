// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using static Microsoft.Diagnostics.ExtensionCommands.TableOutput;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    [Command(Name = "gcroot", Help = "Displays info about references (or roots) to an object at the specified address.")]
    public class GCRootCommand : CommandBase
    {
        private StringBuilder _lineBuilder = new(64);
        private ClrRoot _lastRoot;

        [ServiceImport]
        public ClrRuntime Runtime { get; set; }

        [ServiceImport]
        public RootCacheService RootCache { get; set; }

        [Option(Name="-nostacks", Help ="Do not use stack roots.")]
        public bool NoStacks { get; set; }

        [Argument(Name = "target")]
        public string TargetAddress { get; set; }

        public override void Invoke()
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (!TryParseAddress(TargetAddress, out ulong address))
            {
                throw new ArgumentException($"Could not parse target object address: {TargetAddress:x}");
            }

            ClrObject obj = Runtime.Heap.GetObject(address);
            if (!obj.IsValid)
            {
                Console.WriteWarning($"Warning: {address:x} is not a valid object.");
            }

            GCRoot gcroot = new(Runtime.Heap, (found) =>
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                return found == address;
            });

            int count = 0;
            if (NoStacks)
            {
                foreach (ClrRoot root in RootCache.GetHandleRoots())
                {
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
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                    if (item is not null)
                    {
                        PrintPath(root, item);
                        count++;
                    }
                }
            }
            else
            {
                foreach (ClrRoot root in RootCache.EnumerateRoots())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                    if (item is not null)
                    {
                        PrintPath(root, item);
                        count++;
                    }
                }
            }

            Console.WriteLine($"Found {count:n0} unique roots.");
            Console.WriteLine($"Total time: {sw.Elapsed}");
        }

        private void PrintPath(ClrRoot root, GCRoot.ChainLink link)
        {
            PrintRoot(root);
            //TableOutput objectOutput = new(Console, (2, ""), (16, "x16"))
            //{
            //    AlignLeft = true,
            //    Indent = new(' ', 10)
            //};

            //ulong prevObj = 0;
            while (link != null)
            {
                //bool isDependentHandleLink = RootCache.IsDependentHandleLink(prevObj, link.Object);
                _ = Runtime.Heap.GetObject(link.Object);

                //objectOutput.WriteRow("->", obj.IsValid ? new DmlDumpObj(obj) : obj.Address, obj.Type?.Name ?? "<unknown type>", (isDependentHandleLink ? " (dependent handle)" : ""));

                //prevObj = link.Object;
                link = link.Next;
            }

            Console.WriteLine();
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
            }; ;
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
                else
                {
                    _lineBuilder.Append(':');
                }
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
