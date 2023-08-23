// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrField
    {
        FieldAttributes Attributes { get; }
        IClrType ContainingType { get; }
        ClrElementType ElementType { get; }
        bool IsObjectReference { get; }
        bool IsPrimitive { get; }
        bool IsValueType { get; }
        string? Name { get; }
        int Offset { get; }
        int Size { get; }
        int Token { get; }
        IClrType? Type { get; }

        string? ToString();
    }
}
