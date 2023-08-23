// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrThreadHelpers : IClrThreadHelpers
    {
        private readonly ClrDataProcess _dac;
        private readonly SOSDac _sos;
        private readonly Dictionary<int, string> _regNames = new();

        public IDataReader DataReader { get; }

        public ClrThreadHelpers(ClrDataProcess dac, SOSDac sos, IDataReader dataReader)
        {
            _dac = dac;
            _sos = sos;
            DataReader = dataReader;
        }

        public IEnumerable<ClrStackRoot> EnumerateStackRoots(ClrThread thread)
        {
            using SOSStackRefEnum? stackRefEnum = _sos.EnumerateStackRefs(thread.OSThreadId);
            if (stackRefEnum is null)
                yield break;

            ClrStackFrame[] stack = thread.EnumerateStackTrace().Take(2048).ToArray();

            ClrAppDomain? domain = thread.CurrentAppDomain;
            ClrHeap heap = thread.Runtime.Heap;
            const int GCInteriorFlag = 1;
            const int GCPinnedFlag = 2;
            foreach (StackRefData stackRef in stackRefEnum.ReadStackRefs())
            {
                if (stackRef.Object == 0)
                {
                    Trace.TraceInformation($"EnumerateStackRoots found an entry with Object == 0, addr:{(ulong)stackRef.Address:x} srcType:{stackRef.SourceType:x}");
                    continue;
                }

                bool interior = (stackRef.Flags & GCInteriorFlag) == GCInteriorFlag;
                bool isPinned = (stackRef.Flags & GCPinnedFlag) == GCPinnedFlag;

                ClrStackFrame? frame = stack.FirstOrDefault(f => f.StackPointer == stackRef.Source || f.StackPointer == stackRef.StackPointer && f.InstructionPointer == stackRef.Source);
                frame ??= new ClrStackFrame(thread, null, stackRef.Source, stackRef.StackPointer, ClrStackFrameKind.Unknown, null, null);

                int regOffset = 0;
                string? regName = null;
                if (stackRef.HasRegisterInformation != 0)
                {
                    regOffset = stackRef.Offset;

                    int regIndex = stackRef.Register;
                    if (!_regNames.TryGetValue(regIndex, out regName))
                    {
                        regName = _sos.GetRegisterName(regIndex);
                        if (regName is not null)
                        {
                            _regNames[regIndex] = regName;
                        }
                    }
                }

                if (interior)
                {
                    // Check if the value lives on the heap.
                    ulong obj = stackRef.Object;
                    ClrSegment? segment = heap.GetSegmentByAddress(obj);

                    // If not, this may be a pointer to an object.
                    if (segment is null && DataReader.ReadPointer(obj, out obj))
                        segment = heap.GetSegmentByAddress(obj);

                    // Only yield return if we find a valid object on the heap
                    if (segment is not null)
                        yield return new ClrStackRoot(stackRef.Address, heap.GetObject(obj), isInterior: true, isPinned: isPinned, heap: heap, frame: frame, regName: regName, regOffset: regOffset);
                }
                else
                {
                    // It's possible that heap.GetObjectType could return null and we construct a bad ClrObject, but this should
                    // only happen in the case of heap corruption and obj.IsValidObject will return null, so this is fine.
                    ClrObject obj = heap.GetObject(stackRef.Object);
                    yield return new ClrStackRoot(stackRef.Address, obj, isInterior: false, isPinned: isPinned, heap: heap, frame: frame, regName: regName, regOffset: regOffset);
                }
            }
        }

        public IEnumerable<ClrStackFrame> EnumerateStackTrace(ClrThread thread, bool includeContext)
        {
            using ClrStackWalk? stackwalk = _dac.CreateStackWalk(thread.OSThreadId, 0xf);
            if (stackwalk is null)
                yield break;

            int ipOffset;
            int spOffset;
            int contextSize;
            uint contextFlags = 0;
            if (DataReader.Architecture == Architecture.Arm)
            {
                ipOffset = 64;
                spOffset = 56;
                contextSize = 416;
            }
            else if (DataReader.Architecture == Architecture.Arm64)
            {
                ipOffset = 264;
                spOffset = 256;
                contextSize = 912;
            }
            else if (DataReader.Architecture == Architecture.X86)
            {
                ipOffset = 184;
                spOffset = 196;
                contextSize = 716;
                contextFlags = 0x1003f;
            }
            else // Architecture.X64
            {
                ipOffset = 248;
                spOffset = 152;
                contextSize = 1232;
                contextFlags = 0x10003f;
            }

            Trace.TraceInformation($"BEGIN STACKWALK - {DataReader.Architecture}");

            HResult hr;
            byte[] context = ArrayPool<byte>.Shared.Rent(contextSize);
            do
            {
                hr = stackwalk.GetContext(contextFlags, contextSize, out _, context);
                if (!hr)
                {
                    Trace.TraceInformation($"GetContext failed, flags:{contextFlags:x} size: {contextSize:x} hr={hr}");
                    break;
                }

                ulong ip = context.AsSpan().AsPointer(ipOffset);
                ulong sp = context.AsSpan().AsPointer(spOffset);

                ulong frameVtbl = stackwalk.GetFrameVtable();
                if (frameVtbl != 0)
                {
                    sp = frameVtbl;
                    frameVtbl = DataReader.ReadPointer(sp);
                }

                Trace.TraceInformation($"STACKWALK - hr:{hr}");

                byte[]? contextCopy = null;
                if (includeContext)
                {
                    contextCopy = context.AsSpan(0, contextSize).ToArray();
                }

                ClrStackFrame frame = GetStackFrame(thread, contextCopy, ip, sp, frameVtbl);
                yield return frame;

                hr = stackwalk.Next();
            } while (hr.IsOK);

            Trace.TraceInformation($"END STACKWALK - hr:{hr}");
        }

        private ClrStackFrame GetStackFrame(ClrThread thread, byte[]? context, ulong ip, ulong sp, ulong frameVtbl)
        {
            ClrRuntime runtime = thread.Runtime;

            // todo: pull Method from enclosing type, don't generate methods without a parent
            if (frameVtbl != 0)
            {

                ClrMethod? innerMethod = null;
                string frameName = _sos.GetFrameName(frameVtbl);

                ulong md = _sos.GetMethodDescPtrFromFrame(sp);
                if (md != 0)
                    innerMethod = runtime.GetMethodByHandle(md);

                return new ClrStackFrame(thread, context, ip, sp, ClrStackFrameKind.Runtime, innerMethod, frameName);
            }
            else
            {
                ClrMethod? method = runtime.GetMethodByInstructionPointer(ip);
                return new ClrStackFrame(thread, context, ip, sp, ClrStackFrameKind.ManagedMethod, method, null);
            }
        }
    }
}