// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    public static class ArrayExtensions
    {
        public static string ToHex(this ImmutableArray<byte> array) => string.Concat(array.Select((b) => b.ToString("x2")));

        public static string ToHex(this byte[] array) => string.Concat(array.Select((b) => b.ToString("x2")));
    }
}
