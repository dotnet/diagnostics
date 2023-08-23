// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrException
    {
        ulong Address { get; }
        int HResult { get; }
        IClrException? Inner { get; }
        string? Message { get; }
        ImmutableArray<IClrStackFrame> StackTrace { get; }
        IClrThread? Thread { get; }
        IClrType? Type { get; }

        IClrValue AsObject();
        string ToString();
    }
}