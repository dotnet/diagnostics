// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    internal static class ClrElementTypeExtensions
    {
        public static bool IsPrimitive(this ClrElementType cet)
        {
            return cet is >= ClrElementType.Boolean and <= ClrElementType.Double
                or ClrElementType.NativeInt or ClrElementType.NativeUInt;
        }

        public static bool IsValueType(this ClrElementType cet)
        {
            return IsPrimitive(cet) || cet == ClrElementType.Struct;
        }

        public static bool IsObjectReference(this ClrElementType cet)
        {
            return cet is ClrElementType.String or ClrElementType.Class
                or ClrElementType.Array or ClrElementType.SZArray
                or ClrElementType.Object;
        }
    }
}