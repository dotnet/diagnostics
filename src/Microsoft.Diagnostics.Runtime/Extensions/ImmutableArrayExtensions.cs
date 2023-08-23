// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Runtime
{
    internal static class ImmutableArrayExtensions
    {
        internal static ImmutableArray<T> AsImmutableArray<T>(this T[] array)
        {
            DebugOnly.Assert(Unsafe.SizeOf<T[]>() == Unsafe.SizeOf<ImmutableArray<T>>());
            return Unsafe.As<T[], ImmutableArray<T>>(ref array);
        }

        internal static ImmutableArray<T> MoveOrCopyToImmutable<T>(this ImmutableArray<T>.Builder builder)
        {
            return builder.Capacity == builder.Count ? builder.MoveToImmutable() : builder.ToImmutable();
        }
    }
}