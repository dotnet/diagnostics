// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A frame in a managed stack trace.  Note you can call ToString on an instance of this object to get the
    /// function name (or clr!Frame name) similar to SOS's !clrstack output.
    /// </summary>
    public sealed class ClrStackFrame : IClrStackFrame
    {
        private readonly byte[]? _context;

        /// <summary>
        /// The thread parent of this frame.  Note that this may be null when inspecting the stack of ClrExceptions.
        /// </summary>
        public ClrThread? Thread { get; }

        IClrThread? IClrStackFrame.Thread => Thread;

        /// <summary>
        /// Gets this stack frame context.
        /// </summary>
        public ReadOnlySpan<byte> Context => _context;

        /// <summary>
        /// Gets the instruction pointer of this frame.
        /// </summary>
        public ulong InstructionPointer { get; }

        /// <summary>
        /// Gets the stack pointer of this frame.
        /// </summary>
        public ulong StackPointer { get; }

        /// <summary>
        /// Gets the type of frame (managed or internal).
        /// </summary>
        public ClrStackFrameKind Kind { get; }

        /// <summary>
        /// Gets the <see cref="ClrMethod"/> which corresponds to the current stack frame.  This may be <see langword="null"/> if the
        /// current frame is actually a CLR "Internal Frame" representing a marker on the stack, and that
        /// stack marker does not have a managed method associated with it.
        /// </summary>
        public ClrMethod? Method { get; }

        IClrMethod? IClrStackFrame.Method => Method;

        /// <summary>
        /// Gets the helper method frame name if <see cref="Kind"/> is <see cref="ClrStackFrameKind.Runtime"/>, <see langword="null"/> otherwise.
        /// </summary>
        public string? FrameName { get; }

        public ClrStackFrame(ClrThread? thread, byte[]? context, ulong ip, ulong sp, ClrStackFrameKind kind, ClrMethod? method, string? frameName)
        {
            _context = context;
            Thread = thread;
            InstructionPointer = ip;
            StackPointer = sp;
            Kind = kind;
            Method = method;
            FrameName = frameName;
        }


        public override string? ToString()
        {
            if (Kind == ClrStackFrameKind.ManagedMethod)
                return Method?.Signature;

            int methodLen = 0;
            int methodTypeLen = 0;

            if (Method != null)
            {
                methodLen = Method?.Name?.Length ?? 0;
                if (Method?.Type?.Name != null)
                    methodTypeLen = Method.Type.Name.Length;
            }

            int frameLen = FrameName?.Length ?? 0;
            StringBuilder sb = new(frameLen + methodLen + methodTypeLen + 10);

            sb.Append('[');
            sb.Append(FrameName);
            sb.Append(']');

            if (Method != null)
            {
                sb.Append(" (");

                if (Method.Type != null)
                {
                    sb.Append(Method.Type.Name);
                    sb.Append('.');
                }

                sb.Append(Method.Name);
                sb.Append(')');
            }

            return sb.ToString();
        }
    }
}
