// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrStackFrame
    {
        ReadOnlySpan<byte> Context { get; }
        string? FrameName { get; }
        ulong InstructionPointer { get; }
        ClrStackFrameKind Kind { get; }
        IClrMethod? Method { get; }
        ulong StackPointer { get; }
        IClrThread? Thread { get; }
    }
}
