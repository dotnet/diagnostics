// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    public sealed class ClrStackRoot : ClrRoot, IClrRoot
    {
        private readonly ClrHeap _heap;
        private ClrObject _object;

        internal ClrStackRoot(ulong address, ClrObject obj, bool isInterior, bool isPinned, ClrHeap heap, ClrStackFrame? frame, string? regName, int regOffset)
            : base(address, obj, ClrRootKind.Stack, isInterior, isPinned)
        {
            _heap = heap;
            StackFrame = frame;
            RegisterName = regName;
            RegisterOffset = regOffset;
        }

        IClrStackFrame? IClrRoot.StackFrame => StackFrame;

        public ClrStackFrame? StackFrame { get; }

        public string? RegisterName { get; }

        public int RegisterOffset { get; }

        public override ClrObject Object
        {
            get
            {
                if (_object.Address != 0)
                    return _object;

                ClrObject obj = base.Object;
                if (obj.Type is not null)
                {
                    _object = obj;
                }
                else
                {
                    ClrObject prev = _heap.FindPreviousObjectOnSegment(obj);
                    if (prev.IsValid && prev <= obj && obj < prev + prev.Size)
                    {
                        _object = prev;
                    }
                    else
                    {
                        _object = obj;
                    }
                }

                return _object;
            }
        }
    }
}