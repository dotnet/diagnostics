// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Extensions
{
    internal sealed unsafe class ThreadStackServiceFromDebuggerServices : CallableCOMWrapper, IThreadStackService
    {
        private static readonly Guid IID_IDebuggerThreadStackService = new("AB73D0E6-A5E0-4B5C-B9C1-B312C73C39EE");

        private ref readonly IDebuggerThreadStackServiceVTable VTable =>
            ref System.Runtime.CompilerServices.Unsafe.AsRef<IDebuggerThreadStackServiceVTable>(_vtable);

        public ThreadStackServiceFromDebuggerServices(IntPtr punk)
            : base(IID_IDebuggerThreadStackService, punk)
        {
        }

        public IReadOnlyList<NativeStackFrame> GetStackTrace(uint threadId, int maxFrames)
        {
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfNegative(maxFrames);
#else
            if (maxFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFrames));
            }
#endif
            if (maxFrames == 0)
            {
                return Array.Empty<NativeStackFrame>();
            }

            DebuggerStackFrame[] debuggerFrames = new DebuggerStackFrame[maxFrames];
            HResult result;
            uint framesFilled;
            fixed (DebuggerStackFrame* framesPtr = debuggerFrames)
            {
                result = VTable.GetThreadStackTrace(Self, threadId, framesPtr, debuggerFrames.Length, out framesFilled);
            }
            if (!result.IsOK)
            {
                throw new DiagnosticsException($"Failed to get the native stack trace for thread {threadId:x8}: {result}");
            }
            if (framesFilled > debuggerFrames.Length)
            {
                throw new DiagnosticsException(
                    $"Native stack trace for thread {threadId:x8} returned {framesFilled} frames for a {debuggerFrames.Length}-frame buffer.");
            }

            NativeStackFrame[] frames = new NativeStackFrame[framesFilled];
            for (int index = 0; index < frames.Length; index++)
            {
                DebuggerStackFrame frame = debuggerFrames[index];
                frames[index] = new NativeStackFrame(
                    frame.InstructionPointer,
                    frame.StackPointerValid != 0 ? frame.StackPointer : null);
            }
            return frames;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private readonly struct DebuggerStackFrame
        {
            public readonly ulong InstructionPointer;
            public readonly ulong StackPointer;
            public readonly int StackPointerValid;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private readonly struct IDebuggerThreadStackServiceVTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, DebuggerStackFrame*, int, out uint, int> GetThreadStackTrace;
        }
    }
}
